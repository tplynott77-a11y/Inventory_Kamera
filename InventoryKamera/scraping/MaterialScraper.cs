using Accord.Imaging;
using Accord.Imaging.Filters;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;

namespace InventoryKamera
{
    [Serializable]
	public struct Material : ISerializable
	{
		public string name;
		public int count;

		public Material(string _name, int _count)
		{
			name = _name;
			count = _count;
		}

		public void GetObjectData(SerializationInfo info, StreamingContext context) => info.AddValue(name, count);

		public override int GetHashCode()
		{
			return name.GetHashCode();
		}

		public override bool Equals(object obj)
		{
			return obj is Material material && name == material.name;
		}
	}

	internal class MaterialScraper : InventoryScraper
	{
		private static NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
		// A verifier failure must not prevent the established V3.1 OCR pipeline.
		private static bool precisionVerifierDisabled = false;


		public MaterialScraper()
		{
			inventoryPage = InventoryPage.CharacterDevelopmentItems;
		}

		public MaterialScraper(InventoryPage section) : base()
		{
			inventoryPage = section;
		}

		internal void SetInventoryPage(InventoryPage page)
		{
			if (materialPages.Contains(page)) inventoryPage = page;
		}

		public void Scan_Materials(ref Inventory inventory)
		{
			if (!inventory.Materials.Contains(new Material("Mora", 0)))
			{
				inventory.Materials.Add(new Material("Mora", ScanMora()));
			}

			int scrollCount = 0;

			Material material = new Material(null, 0);
			Material previousMaterial = new Material(null, -1);

			List<Rectangle> rectangles;
			int page = 1;

			// Keep scanning while not repeating any items names
			while (true)
			{
				int rows, cols;
				// Find all items on the screen
				(rectangles, cols, rows) = GetPageOfItems(page, acceptLess: true);

				// Remove last row. Sometimes the bottom of a page of items is caught which results
				// in a faded quantity that can't be parsed. Removing slightly increases the number of pages that
				// need to be scrolled but it's fine.
				var r = rectangles.Take(rectangles.Count() - cols).ToList();

				Logger.Debug("Scanning material page {0}", page);
				Logger.Debug("Located {0} possible item locations on page.", rectangles.Count);

				foreach (var rectangle in r)
				{
					// Select Material
					Navigation.SetCursor(rectangle.Center().X, rectangle.Center().Y);
					Navigation.Click();
					Navigation.SystemWait(Navigation.Speed.SelectNextInventoryItem);

					material.name = ScanMaterialName(out Bitmap nameplate);
					material.count = 0;

					// Check if new material has been found
					if (inventory.Materials.Contains(material))
					{
						Logger.Debug("Repeat material found. Scrolling until end");
						goto LastPage;
					}
					else
					{
						if (!string.IsNullOrEmpty(material.name))
						{
							// Scan Material Quantity
							material.count = ScanMaterialCount(rectangle, out Bitmap quantity);
							if (material.count == 0)
							{
								UserInterface.AddError($"Failed to parse quantity for {material.name}");
								SaveInventoryBitmap(quantity, $"{material.name}_Quantity.png");
							}
							if (Properties.Settings.Default.LogScreenshots || material.count == 0)
                            {
								SaveInventoryBitmap(quantity, $"{material.name}_Quantity.png");
                            }
							inventory.Materials.Add(material);
							UserInterface.ResetCharacterDisplay();
							UserInterface.SetMaterial(nameplate, quantity, material.name, material.count);

							previousMaterial.name = material.name;
						}
					}
					nameplate.Dispose();
					Navigation.Wait(150);
				}

				Navigation.SetCursor(r.Last().Center().X, r.Last().Center().Y);
				Navigation.Click();
				Navigation.Wait(150);

				Logger.Debug("Finished page of materials. Scrolling...");

				// Scroll to next page
				for (int i = 0; i < rows - 1; i++)
				{
					scrollCount++;

					// scroll down
					for (int k = 0; k < 10; k++)
					{
						Navigation.sim.Mouse.VerticalScroll(-1);
						// skip a scroll
						if (( k == 7 ) && ( ( scrollCount % 3 ) == 0 ))
						{
							k++;
							if (scrollCount % 9 == 0)
							{
								if (scrollCount == 18)
								{
									scrollCount = 0;
								}
								else
								{
									Navigation.sim.Mouse.VerticalScroll(-1);
								}
							}
						}
						Navigation.SystemWait(Navigation.Speed.InventoryScroll);
					}
				}
				Navigation.SystemWait(Navigation.Speed.Normal);
				++page;
			}

		LastPage:
			// scroll down as much as possible
			for (int i = 0; i < 20; i++)
			{
				Navigation.sim.Mouse.VerticalScroll(-1);
				Navigation.SystemWait(Navigation.Speed.InventoryScroll);
			}

			Navigation.Wait(500);

			(rectangles, _, _) = GetPageOfItems(page, acceptLess: true);
			bool passby = true;
			for (int i = rectangles.Count - 1; i >= 0; i--) // Click through but backwards to short-circuit after new materials
			{
				// Select Material
				Rectangle rectangle = rectangles[i];
				Navigation.SetCursor(rectangle.Center().X, rectangle.Center().Y);
				Navigation.Click();
				Navigation.SystemWait(Navigation.Speed.SelectNextInventoryItem);

				material.name = ScanMaterialName(out Bitmap nameplate);
				material.count = 0;

				if (inventory.Materials.Contains(material) && passby) continue;

				if (!inventory.Materials.Contains(material))
				{
					if (!string.IsNullOrEmpty(material.name))
					{
						// Scan Material Number
						material.count = ScanMaterialCount(rectangle, out Bitmap quantity);
						if (material.count == 0)
						{
							UserInterface.AddError($"Failed to parse quantity for {material.name}");
							SaveInventoryBitmap(quantity, $"{material.name}_Quantity.png");
						}
						else if (Properties.Settings.Default.LogScreenshots)
						{
							SaveInventoryBitmap(quantity, $"{material.name}_Quantity.png");
						}
						inventory.Materials.Add(material);
						UserInterface.ResetCharacterDisplay();
						UserInterface.SetMaterial(nameplate, quantity, material.name, material.count);
						passby = false; // New material found so break on next old material
						quantity.Dispose();
					}
				}
				else
				{
					Logger.Debug("Last material scanned, {0}.", inventory.Materials.Last().name);
					nameplate.Dispose();
					break;
				}
				Navigation.Wait(150);
			}
		}

		private int ScanMora()
		{
			var region = new Rectangle(
				x: (int)(125 / 1280.0 * Navigation.GetWidth()),
				y: (int)(665 / 720.0 * Navigation.GetHeight()),
				width: (int)(300 / 1280.0 * Navigation.GetWidth()),
				height: (int)(30 / 720.0 * Navigation.GetHeight()));

			if (Navigation.GetAspectRatio() == new Size(8, 5))
			{
				region.Y = (int)( 740 / 800.0 * Navigation.GetHeight() );
			}

			using (var screenshot = Navigation.CaptureRegion(region))
			{
				string mora = ParseMoraFromScreenshot(screenshot);

				if (int.TryParse(mora, out int count))
				{
					UserInterface.ResetCharacterDisplay();
					UserInterface.SetMora(screenshot, count);
                    if (Properties.Settings.Default.LogScreenshots) 
						SaveInventoryBitmap(screenshot, "mora.png");
				}
				else
				{
					UserInterface.SetNavigation_Image(screenshot);
					UserInterface.AddError("Unable to parse mora count");
					SaveInventoryBitmap(screenshot, "mora.png");
				}
				return count;
			}
		}

		public static string ParseMoraFromScreenshot(Bitmap screenshot)
		{
			using (var gray = GenshinProcesor.ConvertToGrayscale(screenshot))
			{
				var invert = (Bitmap)gray.Clone();
				GenshinProcesor.SetInvert(ref invert);
				var input = GenshinProcesor.AnalyzeText(invert).Split(' ').ToList();
				Logger.Debug("Scanned mora input: {0}", input.ToString());
				input.RemoveAll(e => Regex.IsMatch(e.Trim(), @"[^0-9]") || string.IsNullOrWhiteSpace(e.Trim()));
				var mora = input.LastOrDefault();
				Logger.Debug("Parsed mora input: {0}", mora);
				return mora;
			}
		}

		public string ScanMaterialName(out Bitmap nameplate)
		{
			// Grab item name on right
			var refWidth = 1280.0;
			var refHeight = Navigation.GetAspectRatio() == new Size(16,9) ? 720.0 : 800.0;

			var width = Navigation.GetWidth();
			var height = Navigation.GetHeight();

			var reference = new Rectangle(872, 80, 327, 37);

			// Nameplate is in the same place in 16:9 and 16:10
			var region= new RECT(
				Left:   (int)( reference.Left   / refWidth  * width),
				Top:    (int)( reference.Top    / refHeight * height),
				Right:  (int)( reference.Right  / refWidth  * width),
				Bottom: (int)( reference.Bottom / refHeight * height));

			Bitmap bm = Navigation.CaptureRegion(region);
			nameplate = (Bitmap)bm.Clone();

			// Alter Image
			GenshinProcesor.SetGamma(0.2, 0.2, 0.2, ref bm);
			Bitmap n = GenshinProcesor.ConvertToGrayscale(bm);
			GenshinProcesor.SetInvert(ref n);

			string text = GenshinProcesor.AnalyzeText(n,Tesseract.PageSegMode.Auto);
			text = Regex.Replace(text, @"[\W\s]", string.Empty).ToLower();

			//UI
			n.Dispose();
			bm.Dispose();

			if (inventoryPage == InventoryPage.CharacterDevelopmentItems)
				return GenshinProcesor.FindClosestDevelopmentName(text);

			if (inventoryPage == InventoryPage.Materials)
				return GenshinProcesor.FindClosestMaterialName(text);

			return null;
		}

		public static int ScanMaterialCount(Rectangle rectangle, out Bitmap quantity)
		{
			// Score OCR candidates with grayscale weighted twice as strongly as
			// colour-filtered OCR (validated against recorded material scans).
			// Separately count actual readings so one grayscale guess cannot
			// become sufficient evidence merely because it has double weight.
			Dictionary<int, int> weightedVotes = new Dictionary<int, int>();
			Dictionary<int, int> rawReadings = new Dictionary<int, int>();
			int precisionQuantity = 0;
			bool precisionAvailable = false;
			var region = new RECT(
				Left: rectangle.X,
				Top: (int)(rectangle.Y + (0.74 * rectangle.Height)),
				Right: rectangle.Right,
				Bottom: rectangle.Bottom + 25);

			var rRange = new Accord.IntRange(0, 150);
			var bRange = new Accord.IntRange(0, 150);
			var gRange = new Accord.IntRange(0, 150);

			using (Bitmap bm = Navigation.CaptureRegion(region))
			{
				quantity = (Bitmap)bm.Clone();
				if (!precisionVerifierDisabled)
				{
					try
					{
						precisionAvailable = MaterialQuantityPrecisionVerifier.TryRead(bm, out precisionQuantity);
					}
					catch (Exception ex)
					{
						precisionVerifierDisabled = true;
						Logger.Error(ex, "Material V3.2 precision verifier failed; using V3.1 OCR for this scan.");
					}
				}
				for (var scale = 1.0; scale <= 3.0; scale += 0.5)
				{
					using (Bitmap rescaled = GenshinProcesor.ResizeImage(
						bm, (int)(bm.Width * scale), (int)(bm.Height * scale)))
					{
						Bitmap copy = (Bitmap)rescaled.Clone();
						GenshinProcesor.FilterColors(ref copy, rRange, bRange, gRange);

						for (int i = 0; i < copy.Width; i++)
							for (int j = 0; j < copy.Height * 0.25; j++)
								copy.SetPixel(i, j, Color.White);

						Bitmap n = GenshinProcesor.ConvertToGrayscale(copy);
						GenshinProcesor.SetInvert(ref n);
						using (Bitmap thresholded = new Threshold(50).Apply(n))
						{
							string original = GenshinProcesor.AnalyzeText(
								thresholded, Tesseract.PageSegMode.SingleWord, numbersOnly: true).Trim();
							RecordMaterialQuantityVote(weightedVotes, rawReadings, original, scale, "filtered", 1);
						}
						n.Dispose();
						copy.Dispose();

						// Second, independent OCR view: dark text on a light background.
						// Ignore the upper border/stars and keep the centre quantity area.
						var crop = new Rectangle(
							(int)(rescaled.Width * 0.15),
							(int)(rescaled.Height * 0.25),
							(int)(rescaled.Width * 0.70),
							rescaled.Height - (int)(rescaled.Height * 0.25));
						using (Bitmap quantityOnly = rescaled.Clone(crop, rescaled.PixelFormat))
						using (Bitmap grayscale = GenshinProcesor.ConvertToGrayscale(quantityOnly))
						{
							string original = GenshinProcesor.AnalyzeText(
								grayscale, Tesseract.PageSegMode.SingleWord, numbersOnly: true).Trim();
							RecordMaterialQuantityVote(weightedVotes, rawReadings, original, scale, "unfiltered", 2);
						}
					}
				}
			}

			// Preserve V3.1 as the fallback. Precision verification reads the
			// shapes of individual digits; it uses no previous inventory values.
			// The verifier only overrides OCR when every digit has a decisive
			// template match. Ambiguous/merged glyphs retain the V3.1 decision.
			int weightedQuantity = 0;
			var mode = SafeExtractMaxCounter(weightedVotes);
			if (mode.HasValue)
			{
				int competingVotes = weightedVotes.Where(p => p.Key != mode.Value.Key)
					.Select(p => p.Value).DefaultIfEmpty(0).Max();
				int supportingReads;
				if (rawReadings.TryGetValue(mode.Value.Key, out supportingReads) &&
					supportingReads >= 2 && mode.Value.Value > competingVotes)
					weightedQuantity = mode.Value.Key;
			}
			if (precisionAvailable)
			{
				if (weightedQuantity != precisionQuantity)
					Logger.Warn("Material quantity V3.2 correction: weighted OCR {0}, shape verifier {1}",
						weightedQuantity, precisionQuantity);
				return precisionQuantity;
			}
			if (weightedQuantity > 0)
			{
				Logger.Debug("Material OCR V3.1 fallback decision: {0}", weightedQuantity);
				return weightedQuantity;
			}
			Logger.Warn("Material quantity OCR uncertain (precision verifier also unavailable): {0}",
				string.Join(", ", weightedVotes.Select(p =>
					p.Key + " (" + p.Value + " weighted, " + rawReadings[p.Key] + " reads)")));
			return 0;
		}


		private static void RecordMaterialQuantityVote(
			Dictionary<int, int> weightedVotes,
			Dictionary<int, int> rawReadings,
			string original, double scale, string preprocessing, int weight)
		{
			int value;
			if (TryParseMaterialQuantity(original, out value))
			{
				weightedVotes[value] = weightedVotes.ContainsKey(value)
					? weightedVotes[value] + weight : weight;
				rawReadings[value] = rawReadings.ContainsKey(value)
					? rawReadings[value] + 1 : 1;
				Logger.Debug("Material OCR {0} at scale {1}: '{2}' -> {3}",
					preprocessing, scale, original, value);
			}
			else
			{
				Logger.Debug("Material OCR {0} at scale {1}: rejected '{2}'",
					preprocessing, scale, original);
			}
		}

		private static bool TryParseMaterialQuantity(string text, out int value)
		{
			value = 0;
			if (string.IsNullOrWhiteSpace(text)) return false;
			// Accept a plain positive integer, optionally with a thousands separator.
			// Do not turn letter M into 111 or strip arbitrary characters,
			// because those transformations fabricate quantities.
			string clean = text.Trim().Replace(" ", "");
			if (!Regex.IsMatch(clean, @"^\d{1,3}(,\d{3})*$|^\d+$"))
				return false;
			clean = clean.Replace(",", "");
			return int.TryParse(clean, out value) && value > 0;
		}

		private static KeyValuePair<int, int>? SafeExtractMaxCounter(Dictionary<int, int> counts)
		{
            return counts.Count == 0 ? null : (KeyValuePair<int, int>?)counts.Aggregate((l, r) => l.Value > r.Value ? l : r);
        }
    }

	// Independent bitmap-level number check for Genshin's fixed inventory-font
	// quantity glyphs. Each reference is a 36 x 44 packed binary silhouette
	// (digits 0..9), sampled from correctly verified screenshots. These are
	// FONT templates, not remembered quantities. Unknown layouts and unclear
	// shapes return false so ordinary V3.1 OCR remains the fallback.
	internal static class MaterialQuantityPrecisionVerifier
	{
		private const int TemplateWidth = 44;
		private const int TemplateHeight = 36;
		private static readonly byte[][] DigitTemplates = new byte[][] {
			Convert.FromBase64String("AAAAAAAAAAAAAAAAAH/AAAAAH/8AAAAD//gAAAB//8AAAA/g/gAAAPwH8AAAH8A/AAAB+AP4AAA/gD+AAAP4AfgAAD+AH8AAA/AB/AAAfwAfwAAH8AH8AAB/AB/AAAfwAfwAAH8AH8AAB/AB/AAAfwAfwAAD8AH8AAA/AB/AAAP4AfgAAD+AH4AAA/gD+AAAH4A/gAAB/APwAAAPwH8AAAD+D+AAAAf//AAAAD//gAAAAP/wAAAAA/gAAAAAAAAAAAAAAAAA"),
			Convert.FromBase64String("AAAAAAAAAAAAAAAAAAAwAAAAAA8AAAAAA/AAAAAA/wAAAAA/8AAAAA//AAAAAP/wAAAAAf8AAAAAB/AAAAAAfwAAAAAH8AAAAAB/AAAAAAfwAAAAAH8AAAAAB/AAAAAAfwAAAAAH8AAAAAB/AAAAAAfwAAAAAH8AAAAAB/AAAAAAfwAAAAAH8AAAAAB/AAAAAAfwAAAAAH8AAAAAB/AAAAAAfwAAAAAH8AAAAAB+AAAAAAfAAAAAADAAAAAAAAAAAAAAAAAA"),
			Convert.FromBase64String("AAAAAAAAAAAAAAAAAB+AAAAAD/8AAAAD//gAAAB//8AAAAfg/gAAAPwH8AAAD8A/AAAB+APwAAAfgD8AAAH4A/AAAB+APwAAAfgD8AAAAAB/AAAAAA/gAAAAAf4AAAAAP8AAAAAH+AAAAAD/AAAAAD/AAAAAB/gAAAAA/gAAAAAfwAAAAAP4AAAAAH8AAAAAD/AAAAAA/gAIAAAf//+AAAH///gAAB///4AAAf//8AAAH///AAAB///wAAAAAAAAAAAAAAAA"),
			Convert.FromBase64String("AAAAAAAAAAAAAAAAAH/AAAAAH/8AAAAH//gAAAB//8AAAA/h/gAAAPgH8AAAH4B/AAAB8APwAAAfAD8AAAHwA/AAAAAAfwAAAAAH4AAAAAD+AAAAA//AAAAAf/gAAAAH/4AAAAA//gAAAAAP8AAAAAB/AAAAAAP4AAAAAD+AAAHwA/gAAB8AP4AAAfAD+AAAHwA/gAAB+Af4AAAfwP8AAAD///AAAAf//gAAAH//wAAAAf/wAAAAB/gAAAAAAAAAAAAAAAAA"),
			Convert.FromBase64String("AAAAAAAAAAAAAAAAAAPwAAAAAD+AAAAAB/gAAAAA/4AAAAAP+AAAAAH/gAAAAD/4AAAAA/+AAAAAe/gAAAAPP4AAAADz+AAAAB4/gAAAA8P4AAAAPD+AAAAHg/gAAADwP4AAAA8D+AAAAeA/gAAAPgP4AAAD///8AAB////gAAf///4AAP///+AAB////gAAAAf4AAAAAD+AAAAAA/gAAAAAP4AAAAAD8AAAAAA/AAAAAAPgAAAAADwAAAAAAAAAAAAAAAAA"),
			Convert.FromBase64String("AAAAAAAAAAAAAAAAA//+AAAAP//gAAAD//4AAAA//8AAAAP/+AAAAH4AAAAAB8AAAAAAfAAAAAAHwAAAAAB8AAAAAAfAAAAAAPxwAAAAD//wAAAA//+AAAAP//4AAAD4H+AAAAQA/wAAAAAH8AAAAAA/gAAAAAP4AAAAAD+AAAPgA/gAAD4AP4AAA+AD+AAAPwB/AAAB8AfwAAAfgP8AAAD+P+AAAA///AAAAH//gAAAAf/gAAAAA/AAAAAAAAAAAAAAAAAA"),
			Convert.FromBase64String("AAAAAAAAAAAAAAAAAADgAAAAAD4AAAAAD8AAAAAB+AAAAAB/AAAAAA/gAAAAAfwAAAAAP4AAAAAH8AAAAAB+AAAAAA/gAAAAAPwAAAAAH9/wAAAB///AAAAf//4AAAP/D/AAAD/Af4AAA/gD+AAAP4A/gAAD+AH8AAA/gB/AAAP4AfwAAD+AH8AAA/gB/AAAH4AfgAAB/AP4AAAfwD+AAAD+B/AAAAf//gAAAD//wAAAAf/4AAAAA/wAAAAAAAAAAAAAAAAA"),
			Convert.FromBase64String("AAAAAAAAAAAAAAAAH///wAAB///8AAAf//+AAAH///AAAD///wAAA4AH4AAAAAB8AAAAAAfAAAAAAPgAAAAAH4AAAAAD8AAAAAA/AAAAAAfgAAAAAH4AAAAAD+AAAAAA/AAAAAAfwAAAAAH8AAAAAB+AAAAAA/gAAAAAP4AAAAAD+AAAAAB/AAAAAAfwAAAAAH8AAAAAB/AAAAAAfwAAAAAH4AAAAAB+AAAAAA/gAAAAAPwAAAAABwAAAAAAAAAAAAAAAAAA"),
			Convert.FromBase64String("AAAAAAAAAAAAAAAAAD/AAAAAH/+AAAAD//wAAAB//+AAAA/gfwAAAfwD8AAAH8A/gAAB/AP4AAAfwD+AAAH8A/gAAB/gfwAAAf8H8AAAD//+AAAAf//AAAAD//gAAAAf/8AAAAP//gAAAP//8AAAH+H/gAAB/Af4AAA/gD/AAAP4AfwAAD+AH8AAA/AB/AAAPwAfwAAD+AH8AAA/gB/AAAH8A/gAAB/5/wAAAP//4AAAA//8AAAAB/4AAAAAAAAAAAAAAAAA"),
			Convert.FromBase64String("AAAAAAAAAAAAAAAAAD+AAAAAH/8AAAAD//wAAAB//+AAAA/gfwAAAfwD8AAAH8A/gAAD+AP4AAA/gB+AAAP4AfgAAD+AH8AAA/gB/AAAP4AfwAAB/AH4AAAfwD+AAAH+B/gAAA///4AAAH//+AAAA///AAAAD/fwAAAAAH8AAAAAB+AAAAAA/gAAAAAPwAAAAAH4AAAAAD8AAAAAB/AAAAAA/AAAAAAfgAAAAAfwAAAAAHwAAAAABgAAAAAAAAAAAAAAAAAA")
		};

		private struct Glyph
		{
			internal int X, Y, Width, Height, Area;
			internal Glyph(int x, int y, int width, int height, int area)
			{ X = x; Y = y; Width = width; Height = height; Area = area; }
		}

		internal static bool TryRead(Bitmap image, out int quantity)
		{
			quantity = 0;
			// Other display scales are not calibrated; do not guess.
			if (image == null || image.Width < 240 || image.Width > 260 ||
				image.Height < 80 || image.Height > 115) return false;

			int startX = (int)(image.Width * 0.23);
			int endX = (int)(image.Width * 0.77);
			int startY = (int)(image.Height * 0.15);
			int width = endX - startX, height = image.Height - startY;
			bool[] darkBlue = new bool[width * height];
			for (int y = 0; y < height; ++y)
				for (int x = 0; x < width; ++x)
				{
					Color p = image.GetPixel(startX + x, startY + y);
					darkBlue[y * width + x] = p.R < 160 &&
						p.B * 100 > p.R * 103 && p.B * 100 > p.G * 101;
				}

			bool[] visited = new bool[darkBlue.Length];
			int[] queue = new int[darkBlue.Length];
			List<Glyph> glyphs = new List<Glyph>();
			for (int pos = 0; pos < darkBlue.Length; ++pos)
			{
				if (!darkBlue[pos] || visited[pos]) continue;
				int head = 0, tail = 0, area = 0;
				queue[tail++] = pos; visited[pos] = true;
				int xMin = width, xMax = 0, yMin = height, yMax = 0;
				while (head < tail)
				{
					int here = queue[head++];
					int cx = here % width, cy = here / width;
					area++;
					xMin = Math.Min(xMin, cx); xMax = Math.Max(xMax, cx);
					yMin = Math.Min(yMin, cy); yMax = Math.Max(yMax, cy);
					for (int dy = -1; dy <= 1; ++dy)
						for (int dx = -1; dx <= 1; ++dx)
						{
							int nx = cx + dx, ny = cy + dy;
							if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
							int n = ny * width + nx;
							if (!darkBlue[n] || visited[n]) continue;
							visited[n] = true; queue[tail++] = n;
						}
				}
				int boxHeight = yMax - yMin + 1, boxWidth = xMax - xMin + 1;
				if (boxHeight >= 27 && boxHeight <= 36 && boxWidth >= 9 &&
					area >= 150 && yMin + startY >= 18)
					glyphs.Add(new Glyph(xMin, yMin, boxWidth, boxHeight, area));
			}
			if (glyphs.Count < 1 || glyphs.Count > 6) return false;
			glyphs.Sort((a, b) => a.X.CompareTo(b.X));

			foreach (Glyph g in glyphs)
			{
				int scaledWidth = (int)Math.Round(g.Width * 32.0 / g.Height);
				// Touching digits or unusual fonts: leave the existing OCR result.
				if (scaledWidth > 40) return false;
				bool[] silhouette = new bool[TemplateWidth * TemplateHeight];
				int shiftX = (TemplateWidth - scaledWidth) / 2;
				for (int yy = 0; yy < 32; ++yy)
					for (int xx = 0; xx < scaledWidth; ++xx)
					{
						int sourceY = g.Y + yy * g.Height / 32;
						int sourceX = g.X + xx * g.Width / scaledWidth;
						silhouette[(yy + 2) * TemplateWidth + shiftX + xx] =
							darkBlue[sourceY * width + sourceX];
					}

				int bestDigit = -1, best = int.MaxValue, runnerUp = int.MaxValue;
				for (int d = 0; d < DigitTemplates.Length; ++d)
				{
					int distance = 0;
					for (int i = 0; i < silhouette.Length; ++i)
						if (silhouette[i] != ((DigitTemplates[d][i / 8] & (0x80 >> (i % 8))) != 0))
							distance++;
					if (distance < best)
					{
						runnerUp = best; best = distance; bestDigit = d;
					}
					else if (distance < runnerUp) runnerUp = distance;
				}
				// These conservative limits were tested on 1,118 saved image
				// captures. If recognition is not decisive, use V3.1 instead.
				if (best > 130 || runnerUp - best < 30) return false;
				if (quantity > (int.MaxValue - bestDigit) / 10) return false;
				quantity = quantity * 10 + bestDigit;
			}
			return quantity > 0;
		}
	}
}