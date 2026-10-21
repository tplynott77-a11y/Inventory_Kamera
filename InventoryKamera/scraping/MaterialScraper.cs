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

			// Require at least two real OCR readings of the chosen quantity,
			// and a unique winner after weighting; otherwise flag uncertainty.
			var mode = SafeExtractMaxCounter(weightedVotes);
			if (mode.HasValue)
			{
				int competingVotes = weightedVotes.Where(p => p.Key != mode.Value.Key)
					.Select(p => p.Value).DefaultIfEmpty(0).Max();
				int supportingReads;
				if (rawReadings.TryGetValue(mode.Value.Key, out supportingReads) &&
					supportingReads >= 2 && mode.Value.Value > competingVotes)
				{
					Logger.Debug("Material OCR decision: {0} ({1} weighted votes, {2} actual reads)",
						mode.Value.Key, mode.Value.Value, supportingReads);
					return mode.Value.Key;
				}
			}

			Logger.Warn("Material quantity OCR uncertain, weighted candidates: {0}",
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
}