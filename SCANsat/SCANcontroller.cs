#region license
/* 
 * [Scientific Committee on Advanced Navigation]
 * 			S.C.A.N. Satellite
 *
 * SCANcontroller - scenario module that handles all scanning
 * 
 * Copyright (c)2013 damny;
 * Copyright (c)2014 David Grandy <david.grandy@gmail.com>;
 * Copyright (c)2014 technogeeky <technogeeky@gmail.com>;
 * Copyright (c)2014 (Your Name Here) <your email here>; see LICENSE.txt for licensing details.
 */
#endregion
using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using SCANsat.SCAN_UI;
using SCANsat.SCAN_Data;
using SCANsat.SCAN_Platform;
using SCANsat.SCAN_Platform.Palettes;
using SCANsat.SCAN_Platform.Palettes.ColorBrewer;
using SCANsat.SCAN_Platform.Palettes.FixedColors;
using SCANsat.SCAN_Toolbar;
using palette = SCANsat.SCAN_UI.UI_Framework.SCANpalette;

namespace SCANsat
{
	[KSPScenario(ScenarioCreationOptions.AddToAllGames | ScenarioCreationOptions.AddToExistingGames, GameScenes.FLIGHT, GameScenes.SPACECENTER, GameScenes.TRACKSTATION)]
	public class SCANcontroller : ScenarioModule
	{
		public static SCANcontroller controller
		{
			get
			{
				Game g = HighLogic.CurrentGame;
				if (g == null) return null;
				try
				{
					var mod = g.scenarios.FirstOrDefault(m => m.moduleName == typeof(SCANcontroller).Name);
					if (mod != null)
						return (SCANcontroller)mod.moduleRef;
					else
						return null;
				}
				catch (Exception e)
				{
					SCANUtil.SCANlog("Could not find SCANsat Scenario Module: {0}", e);
					return null;
				}
			}
		}

		private static int minScanAlt = 5000;
		private static int maxScanAlt = 500000;
		private static int bestScanAlt = 250000;
		[KSPField(isPersistant = true)]
		public int colours = 0;
		[KSPField(isPersistant = true)]
		public bool map_markers = true;
		[KSPField(isPersistant = true)]
		public bool map_flags = true;
		[KSPField(isPersistant = true)]
		public bool map_orbit = true;
		[KSPField(isPersistant = true)]
		public bool map_asteroids = true;
		[KSPField(isPersistant = true)]
		public bool map_grid = true;
		[KSPField(isPersistant = true)]
		public bool map_ResourceOverlay = false; //Is the overlay activated for the selected resource
		[KSPField(isPersistant = true)]
		public int projection = 0;
		[KSPField(isPersistant = true)]
		public int map_width = 0;
		[KSPField(isPersistant = true)]
		public int map_x = 100;
		[KSPField(isPersistant = true)]
		public int map_y = 50;
		[KSPField(isPersistant = true)]
		public string anomalyMarker = "✗";
		public string closeBox = "✖";
		[KSPField(isPersistant = true)]
		public bool legend = false;
		[KSPField(isPersistant = true)]
		public bool scan_background = true;
		[KSPField(isPersistant = true)]
		public int timeWarpResolution = 20;
		[KSPField(isPersistant = true)]
		public string resourceSelection;
		[KSPField(isPersistant = true)]
		public int resourceOverlayType = 0; //0 for ORS, 1 for Kethane
		[KSPField(isPersistant = true)]
		public bool dataRebuild = true;
		[KSPField(isPersistant = true)]
		public bool mainMapVisible = false;
		[KSPField(isPersistant = true)]
		public bool bigMapVisible = false;
		[KSPField(isPersistant = true)]
		public bool kscMapVisible = false;
		[KSPField(isPersistant = true)]
		public bool toolTips = true;
		[KSPField(isPersistant = true)]
		public bool useStockAppLauncher = true;
		[KSPField(isPersistant = true)]
		public bool regolithBiomeLock = false;
		[KSPField(isPersistant = true)]
		public bool useStockBiomes = false;
		[KSPField(isPersistant = true)]
		public float biomeTransparency = 40;

		/* Available resources for overlays; loaded from resource addon configs; only loaded once */
		private static Dictionary<string, SCANresourceGlobal> masterResourceNodes;

		/* Resource types loaded from configs; only needs to be loaded once */
		private static Dictionary<string, SCANresourceType> resourceTypes;

		private static Dictionary<string, SCANterrainConfig> masterTerrainNodes;

		/* Primary SCANsat vessel dictionary; loaded every time */
		private Dictionary<Guid, SCANvessel> knownVessels = new Dictionary<Guid, SCANvessel>();

		/* Primary SCANdata dictionary; loaded every time; static to protect against null SCANcontroller instance */
		private static Dictionary<string, SCANdata> body_data = new Dictionary<string,SCANdata>();

		/* Kethane integration */
		private bool kethaneRebuild, kethaneReset, kethaneBusy = false;

		/* UI window objects */
		internal SCAN_MBW mainMap;
		internal SCAN_MBW settingsWindow;
		internal SCAN_MBW instrumentsWindow;
		internal SCAN_MBW BigMap;
		internal SCAN_MBW kscMap;
		internal SCAN_MBW colorManager;

		/* App launcher object */
		internal SCANappLauncher appLauncher;

		/* Used in case the loading process is interupted somehow */
		private bool loaded = false;

		/* Governs resource overlay availability */
		//private static bool globalResourceOverlay = false;

		private readonly Color defaultLowBiomeColor = palette.xkcd_CamoGreen;
		private readonly Color defaultHighBiomeColor = palette.xkcd_Marigold;

		private Color lowBiomeColor = palette.xkcd_CamoGreen;
		private Color highBiomeColor = palette.xkcd_Marigold;

		#region Public Accessors

		public static Dictionary<string, SCANdata> Body_Data
		{
			get { return body_data; }
		}

		/* Use this method to protect against duplicate dictionary keys */
		public void addToBodyData (CelestialBody b, SCANdata data)
		{
			if (!body_data.ContainsKey(b.name))
				body_data.Add(b.name, data);
			else
				Debug.LogError("[SCANsat] Warning: SCANdata Dictionary Already Contains Key of This Type");
		}

		public static Dictionary<string, SCANterrainConfig> MasterTerrainNodes
		{
			get { return masterTerrainNodes; }
			internal set { masterTerrainNodes = value; }
		}

		public static void addToTerrainConfigData (string name, SCANterrainConfig data)
		{
			if (!masterTerrainNodes.ContainsKey(name))
				masterTerrainNodes.Add(name, data);
			else
				Debug.LogError("[SCANsat] Warning: SCANterrain Data Dictionary Already Contains Key Of This Type");
		}

		public static Dictionary<string, SCANresourceGlobal> MasterResourceNodes
		{
			get { return masterResourceNodes; }
			internal set { masterResourceNodes = value; }
		}
		
		public static void addToResourceData (string name, SCANresourceGlobal res)
		{
			if (!masterResourceNodes.ContainsKey(name))
			{
				masterResourceNodes.Add(name, res);
			}
			else
				Debug.LogError(string.Format("[SCANsat] Warning: SCANResource Dictionary Already Contains Key of This Type: Resource: {0}", name));
		}

		public static Dictionary<string, SCANresourceType> ResourceTypes
		{
			get { return resourceTypes; }
			internal set { resourceTypes = value; }
		}

		public Dictionary<Guid, SCANvessel> Known_Vessels
		{
			get { return knownVessels; }
		}

		public bool KethaneBusy
		{
			get { return kethaneBusy; }
			set { kethaneBusy = value; }
		}

		public bool KethaneReset
		{
			get { return kethaneReset; }
			internal set { kethaneReset = value; }
		}

		public bool KethaneRebuild
		{
			get { return kethaneRebuild; }
			internal set { kethaneRebuild = value; }
		}

		public int ActiveSensors
		{
			get { return activeSensors; }
		}

		public int ActiveVessels
		{
			get { return activeVessels; }
		}

		public int ActualPasses
		{
			get { return actualPasses; }
		}

		public Color LowBiomeColor
		{
			get { return lowBiomeColor; }
			internal set { lowBiomeColor = value; }
		}

		public Color HighBiomeColor
		{
			get { return highBiomeColor; }
			internal set { highBiomeColor = value; }
		}

		public Color DefaultLowBiomeColor
		{
			get { return defaultLowBiomeColor; }
		}

		public Color DefaultHighBiomeColor
		{
			get { return defaultHighBiomeColor; }
		}

		#endregion

		public override void OnLoad(ConfigNode node)
		{
			body_data = new Dictionary<string, SCANdata>();
			if (node.HasValue("BiomeLowColor"))
			{
				try
				{
					lowBiomeColor = lowBiomeColor.FromHex(node.GetValue("BiomeLowColor"));
				}
				catch (Exception e)
				{
					SCANUtil.SCANlog("Low Biome Color Format Incorrect; Reverting To Default: {0}", e);
					lowBiomeColor = palette.xkcd_CamoGreen;
				}
			}
			if (node.HasValue("BiomeHighColor"))
			{
				try
				{
					highBiomeColor = highBiomeColor.FromHex(node.GetValue("BiomeHighColor"));
				}
				catch (Exception e)
				{
					SCANUtil.SCANlog("High Biome Color Format Incorrect; Reverting To Default: {0}", e);
					highBiomeColor = palette.xkcd_Marigold;
				}
			}
			ConfigNode node_vessels = node.GetNode("Scanners");
			if (node_vessels != null)
			{
				SCANUtil.SCANlog("SCANsat Controller: Loading {0} known vessels", node_vessels.CountNodes);
				foreach (ConfigNode node_vessel in node_vessels.GetNodes("Vessel"))
				{
					Guid id;
					try
					{
						id = new Guid(node_vessel.GetValue("guid"));
					}
					catch (Exception e)
					{
						SCANUtil.SCANlog("Something Went Wrong Loading This SCAN Vessel; Moving On To The Next: {0}", e);
						continue;
					}
					foreach (ConfigNode node_sensor in node_vessel.GetNodes("Sensor"))
					{
						int sensor;
						double fov, min_alt, max_alt, best_alt;
						if (!int.TryParse(node_sensor.GetValue("type"), out sensor))
							sensor = 0;
						if (!double.TryParse(node_sensor.GetValue("fov"), out fov))
							fov = 3;
						if (!double.TryParse(node_sensor.GetValue("min_alt"), out min_alt))
							min_alt = minScanAlt;
						if (!double.TryParse(node_sensor.GetValue("max_alt"), out max_alt))
							max_alt = maxScanAlt;
						if (!double.TryParse(node_sensor.GetValue("best_alt"), out best_alt))
							best_alt = bestScanAlt;
						registerSensor(id, (SCANtype)sensor, fov, min_alt, max_alt, best_alt);
					}
				}
			}

			ConfigNode node_progress = node.GetNode("Progress");
			if (node_progress != null)
			{
				foreach (ConfigNode node_body in node_progress.GetNodes("Body"))
				{
					float min, max, clamp;
					float? clampState = null;
					Palette dataPalette;
					string paletteName = "";
					int pSize;
					bool pRev, pDis, disabled;
					string body_name = node_body.GetValue("Name");
					SCANUtil.SCANlog("SCANsat Controller: Loading map for {0}", body_name);
					CelestialBody body = FlightGlobals.Bodies.FirstOrDefault(b => b.name == body_name);
					if (body != null)
					{
						SCANdata data = SCANUtil.getData(body);
						if (data == null)
							data = new SCANdata(body);
						if (!body_data.ContainsKey(body_name))
							body_data.Add(body_name, data);
						else
							body_data[body_name] = data;
						try
						{
							string mapdata = node_body.GetValue("Map");
							if (dataRebuild)
							{ //On the first load deserialize the "Map" value to both coverage arrays
								data.integerDeserialize(mapdata, true);
							}
							else
							{
								data.integerDeserialize(mapdata, false);
							}
						}
						catch (Exception e)
						{
							SCANUtil.SCANlog("Something Went Wrong Loading Scanning Data; Resetting Coverage: {0}", e);
							data.reset();
							// fail somewhat gracefully; don't make the save unloadable 
						}
						try // Make doubly sure that nothing here can interupt the Scenario Module loading process
						{
							//Verify that saved data types can be converted, revert to default values otherwise
							if (bool.TryParse(node_body.GetValue("Disabled"), out disabled))
								data.Disabled = disabled;
							if (!float.TryParse(node_body.GetValue("MinHeightRange"), out min))
								min = data.DefaultMinHeight;
							if (!float.TryParse(node_body.GetValue("MaxHeightRange"), out max))
								max = data.DefaultMaxHeight;
							if (node_body.HasValue("ClampHeight"))
							{
								if (float.TryParse(node_body.GetValue("ClampHeight"), out clamp))
									clampState = clamp;
							}
							if (!int.TryParse(node_body.GetValue("PaletteSize"), out pSize))
								pSize = data.DefaultColorPalette.size;
							if (!bool.TryParse(node_body.GetValue("PaletteReverse"), out pRev))
								pRev = data.DefaultReversePalette;
							if (!bool.TryParse(node_body.GetValue("PaletteDiscrete"), out pDis))
								pDis = false;
							if (node_body.HasValue("PaletteName"))
								paletteName = node_body.GetValue("PaletteName");
							dataPalette = SCANconfigLoader.paletteLoader(paletteName, pSize);
							if (dataPalette == PaletteLoader.defaultPalette)
							{
								paletteName = "Default";
								pSize = 7;
							}

							SCANterrainConfig dataTerrainConfig = new SCANterrainConfig(min, max, clampState, dataPalette, pSize, pRev, pDis, body);

							if (masterTerrainNodes.ContainsKey(body.name))
								masterTerrainNodes[body.name] = dataTerrainConfig;
							else
								addToTerrainConfigData(body.name, dataTerrainConfig);

							data.TerrainConfig = dataTerrainConfig;
						}
						catch (Exception e)
						{
							SCANUtil.SCANlog("Error Loading SCANdata; Reverting To Default Settings: {0}", e);
						}
					}
				}
			}
			dataRebuild = false; //Used for the one-time update to the new integer array

			if (SCANconfigLoader.GlobalResource)
			{
				if (string.IsNullOrEmpty(resourceSelection))
					resourceSelection = masterResourceNodes.ElementAt(0).Key;
				else if (!masterResourceNodes.ContainsKey(resourceSelection))
					resourceSelection = masterResourceNodes.ElementAt(0).Key;
			}
			//try
			//{
			//	if (resourceTypes == null)
			//		SCANUtil.loadSCANtypes();
			//}
			//catch (Exception e)
			//{
			//	SCANUtil.SCANlog("Something Went Wrong Loading Resource Configs: {0}", e);
			//}
			//try
			//{
			//	if (resourceList == null)
			//		loadResources();
			//}
			//catch (Exception e)
			//{
			//	SCANUtil.SCANlog("Something Went Wrong Loading Resource Data: {0}", e);
			//}
			ConfigNode node_resources = node.GetNode("SCANResources");
			if (node_resources != null)
			{
				foreach(ConfigNode node_resource_type in node_resources.GetNodes("ResourceType"))
				{
					if (node_resource_type != null)
					{
						string name = node_resource_type.GetValue("Resource");
						string lowColor = node_resource_type.GetValue("MinColor");
						string highColor = node_resource_type.GetValue("MaxColor");
						string transparent = node_resource_type.GetValue("Transparency");
						string minMaxValues = node_resource_type.GetValue("MinMaxValues");
						loadCustomResourceValues(minMaxValues, name, lowColor, highColor, transparent);
					}
				}
			}
			try
			{
				if (HighLogic.LoadedScene == GameScenes.FLIGHT)
				{
					mainMap = gameObject.AddComponent<SCANmainMap>();
					settingsWindow = gameObject.AddComponent<SCANsettingsUI>();
					instrumentsWindow = gameObject.AddComponent<SCANinstrumentUI>();
					colorManager = gameObject.AddComponent<SCANcolorSelection>();
					BigMap = gameObject.AddComponent<SCANBigMap>();
				}
				else if (HighLogic.LoadedScene == GameScenes.SPACECENTER || HighLogic.LoadedScene == GameScenes.TRACKSTATION)
				{
					kscMap = gameObject.AddComponent<SCANkscMap>();
					settingsWindow = gameObject.AddComponent<SCANsettingsUI>();
					colorManager = gameObject.AddComponent<SCANcolorSelection>();
				}
			}
			catch (Exception e)
			{
				SCANUtil.SCANlog("Something Went Wrong Initializing UI Objects: {0}", e);
			}
			loaded = true;
		}

		public override void OnSave(ConfigNode node)
		{
			if (HighLogic.LoadedScene != GameScenes.EDITOR)
			{
				node.AddValue("BiomeLowColor", lowBiomeColor.ToHex());
				node.AddValue("BiomeHighColor", highBiomeColor.ToHex());
				ConfigNode node_vessels = new ConfigNode("Scanners");
				foreach (Guid id in knownVessels.Keys)
				{
					ConfigNode node_vessel = new ConfigNode("Vessel");
					node_vessel.AddValue("guid", id.ToString());
					if (knownVessels[id].vessel != null) node_vessel.AddValue("name", knownVessels[id].vessel.vesselName); // not read
					foreach (SCANsensor sensor in knownVessels[id].sensors.Values)
					{
						ConfigNode node_sensor = new ConfigNode("Sensor");
						node_sensor.AddValue("type", (int)sensor.sensor);
						node_sensor.AddValue("fov", sensor.fov);
						node_sensor.AddValue("min_alt", sensor.min_alt);
						node_sensor.AddValue("max_alt", sensor.max_alt);
						node_sensor.AddValue("best_alt", sensor.best_alt);
						node_vessel.AddNode(node_sensor);
					}
					node_vessels.AddNode(node_vessel);
				}
				node.AddNode(node_vessels);
				if (body_data != null)
				{
					ConfigNode node_progress = new ConfigNode("Progress");
					foreach (string body_name in body_data.Keys)
					{
						ConfigNode node_body = new ConfigNode("Body");
						SCANdata body_scan = body_data[body_name];
						node_body.AddValue("Name", body_name);
						node_body.AddValue("Disabled", body_scan.Disabled);
						node_body.AddValue("MinHeightRange", body_scan.TerrainConfig.MinTerrain);
						node_body.AddValue("MaxHeightRange", body_scan.TerrainConfig.MaxTerrain);
						if (body_scan.TerrainConfig.ClampTerrain != null)
							node_body.AddValue("ClampHeight", body_scan.TerrainConfig.ClampTerrain);
						node_body.AddValue("PaletteName", body_scan.TerrainConfig.ColorPal.name);
						node_body.AddValue("PaletteSize", body_scan.TerrainConfig.PalSize);
						node_body.AddValue("PaletteReverse", body_scan.TerrainConfig.PalRev);
						node_body.AddValue("PaletteDiscrete", body_scan.TerrainConfig.PalDis);
						node_body.AddValue("Map", body_scan.integerSerialize());
						node_progress.AddNode(node_body);
					}
					node.AddNode(node_progress);
				}
				if (resourceTypes.Count > 0 && masterResourceNodes.Count > 0)
				{
					ConfigNode node_resources = new ConfigNode("SCANResources");
					foreach (SCANresourceType t in resourceTypes.Values)
					{
						SCANresourceGlobal r = null;

						if (masterResourceNodes.ContainsKey(t.Name))
							r = masterResourceNodes[t.Name];

						if (r != null)
						{
							ConfigNode node_resource_type = new ConfigNode("ResourceType");
							node_resource_type.AddValue("Resource", r.Name);
							node_resource_type.AddValue("MinColor", r.MinColor.ToHex());
							node_resource_type.AddValue("MaxColor", r.MaxColor.ToHex());
							node_resource_type.AddValue("Transparency", r.Transparency * 100);

							string rMinMax = saveResources(t);
							node_resource_type.AddValue("MinMaxValues", rMinMax);
							node_resources.AddNode(node_resource_type);
						}
					}
					node.AddNode(node_resources);
				}
			}
		}

		private void Start()
		{
			GameEvents.onVesselSOIChanged.Add(SOIChange);
			if (HighLogic.LoadedScene == GameScenes.FLIGHT)
			{
				if (!body_data.ContainsKey(FlightGlobals.currentMainBody.name))
				body_data.Add(FlightGlobals.currentMainBody.name, new SCANdata(FlightGlobals.currentMainBody));
			}
			else if (HighLogic.LoadedScene == GameScenes.SPACECENTER || HighLogic.LoadedScene == GameScenes.TRACKSTATION)
			{
				if (!body_data.ContainsKey(Planetarium.fetch.Home.name))
					body_data.Add(Planetarium.fetch.Home.name, new SCANdata(Planetarium.fetch.Home));
			}
			if (useStockAppLauncher)
				appLauncher = gameObject.AddComponent<SCANappLauncher>();
		}

		private void Update()
		{
			if (scan_background && loaded)
			{
				scanFromAllVessels();
			}
		}

		private void OnDestroy()
		{
			GameEvents.onVesselSOIChanged.Remove(SOIChange);
			if (mainMap != null)
				Destroy(mainMap);
			if (settingsWindow != null)
				Destroy(settingsWindow);
			if (instrumentsWindow != null)
				Destroy(instrumentsWindow);
			if (kscMap != null)
				Destroy(kscMap);
			if (BigMap != null)
				Destroy(BigMap);
			if (appLauncher != null)
				Destroy(appLauncher);
		}

		private void SOIChange(GameEvents.HostedFromToAction<Vessel, CelestialBody> VC)
		{
			if (!body_data.ContainsKey(VC.to.name))
				body_data.Add(VC.to.name, new SCANdata(VC.to));
		}

		private string saveResources(SCANresourceType type)
		{
			List<string> sL = new List<string>();
			if (masterResourceNodes.ContainsKey(type.Name))
			{
				SCANresourceGlobal r = masterResourceNodes[type.Name];
				foreach (SCANresourceBody bodyRes in r.BodyConfigs.Values)
				{
					string a = string.Format("{0}|{1:F3}|{2:F3}", bodyRes.Body.flightGlobalsIndex, bodyRes.MinValue, bodyRes.MaxValue);
					sL.Add(a);
				}
			}

			return string.Join(",", sL.ToArray());
		}

		private void loadCustomResourceValues(string s, string resource, string low, string high, string trans)
		{
			SCANresourceGlobal r;

			if (masterResourceNodes.ContainsKey(resource))
				r = masterResourceNodes[resource];
			else
				return;

			Color lowColor = new Color();
			Color highColor = new Color();
			float transparent;
			if (!SCANUtil.loadColor(ref lowColor, low))
			{
				if (resourceTypes.ContainsKey(resource))
					lowColor = resourceTypes[resource].ColorEmpty;
				else
					lowColor = palette.xkcd_DarkPurple;
			}
			if (!SCANUtil.loadColor(ref highColor, high))
			{
				if (resourceTypes.ContainsKey(resource))
					highColor = resourceTypes[resource].ColorFull;
				else
					highColor = palette.xkcd_Lemon;
			}

			if (!float.TryParse(trans, out transparent))
				transparent = 40f;

			r.MinColor = lowColor;
			r.MaxColor = highColor;
			r.Transparency = transparent;

			if (!string.IsNullOrEmpty(s))
			{
				string[] sA = s.Split(',');
				for (int i = 0; i < sA.Length; i++)
				{
					string[] sB = sA[i].Split('|');
					try
					{
						int j = 0;
						float min = 0;
						float max = 0;
						if (!int.TryParse(sB[0], out j))
							continue;
						CelestialBody b;
						if ((b = FlightGlobals.Bodies.FirstOrDefault(a => a.flightGlobalsIndex == j)) != null)
						{
							if (r.BodyConfigs.ContainsKey(b.name))
							{
								SCANresourceBody res = r.BodyConfigs[b.name];
								if (!float.TryParse(sB[1], out min))
									min = res.DefaultMinValue;
								if (!float.TryParse(sB[2], out max))
									max = res.DefaultMaxValue;
								res.MinValue = min;
								res.MaxValue = max;
							}
							else
								SCANUtil.SCANlog("No resources found assigned for Celestial Body: {0}, skipping...", b.name);
						}
						else
							SCANUtil.SCANlog("No Celestial Body found matching this saved resource value: {0}, skipping...", j);
					}
					catch (Exception e)
					{
						SCANUtil.SCANlog("Something Went Wrong While Loading Custom Resource Settings; Reverting To Default Values: {0}", e);
					}
				}
			}
		}

		public class SCANsensor
		{
			public SCANtype sensor;
			public double fov;
			public double min_alt, max_alt, best_alt;

			public bool inRange;
			public bool bestRange;
		}

		public class SCANvessel
		{
			public Guid id;
			public Vessel vessel;
			public Dictionary<SCANtype, SCANsensor> sensors = new Dictionary<SCANtype, SCANsensor>();

			public CelestialBody body;
			public double latitude, longitude;
			public int frame;
			public double lastUT;
		}

		internal void registerSensor(Vessel v, SCANtype sensors, double fov, double min_alt, double max_alt, double best_alt)
		{
			registerSensor(v.id, sensors, fov, min_alt, max_alt, best_alt);
			knownVessels[v.id].vessel = v;
			knownVessels[v.id].latitude = SCANUtil.fixLatShift(v.latitude);
			knownVessels[v.id].longitude = SCANUtil.fixLonShift(v.longitude);
		}

		private void registerSensor(Guid id, SCANtype sensors, double fov, double min_alt, double max_alt, double best_alt)
		{
			if (id == null)
				return;
			if (!knownVessels.ContainsKey(id)) knownVessels[id] = new SCANvessel();
			SCANvessel sv = knownVessels[id];
			sv.id = id;
			sv.vessel = FlightGlobals.Vessels.FirstOrDefault(a => a.id == id);
			if (sv.vessel == null)
			{
				knownVessels.Remove(id);
				return;
			}
			foreach (SCANtype sensor in Enum.GetValues(typeof(SCANtype)))
			{
				if (SCANUtil.countBits((int)sensor) != 1) continue;
				if ((sensor & sensors) == SCANtype.Nothing) continue;
				double this_fov = fov, this_min_alt = min_alt, this_max_alt = max_alt, this_best_alt = best_alt;
				if (this_max_alt <= 0)
				{
					this_min_alt = 5000;
					this_max_alt = 500000;
					this_best_alt = 200000;
					this_fov = 5;
					if ((sensor & SCANtype.AltimetryHiRes) != SCANtype.Nothing) this_fov = 3;
					if ((sensor & SCANtype.AnomalyDetail) != SCANtype.Nothing)
					{
						this_min_alt = 0;
						this_max_alt = 2000;
						this_best_alt = 0;
						this_fov = 1;
					}
				}
				if (!sv.sensors.ContainsKey(sensor)) sv.sensors[sensor] = new SCANsensor();
				SCANsensor s = sv.sensors[sensor];
				s.sensor = sensor;
				s.fov = this_fov;
				s.min_alt = this_min_alt;
				s.max_alt = this_max_alt;
				s.best_alt = this_best_alt;
			}
		}

		internal void unregisterSensor(Vessel v, SCANtype sensors)
		{
			if (!knownVessels.ContainsKey(v.id)) return;
			SCANvessel sv = knownVessels[v.id];
			sv.id = v.id;
			sv.vessel = v;
			foreach (SCANtype sensor in Enum.GetValues(typeof(SCANtype)))
			{
				if ((sensors & sensor) == SCANtype.Nothing) continue;
				if (!sv.sensors.ContainsKey(sensor)) continue;
				sv.sensors.Remove(sensor);
			}
		}

		internal bool isVesselKnown(Guid id, SCANtype sensor)
		{
			if (!knownVessels.ContainsKey(id)) return false;
			SCANtype all = SCANtype.Nothing;
			foreach (SCANtype s in knownVessels[id].sensors.Keys) all |= s;
			return (all & sensor) != SCANtype.Nothing;
		}

		private bool isVesselKnown(Guid id)
		{
			if (!knownVessels.ContainsKey(id)) return false;
			return knownVessels[id].sensors.Count > 0;
		}

		private bool isVesselKnown(Vessel v)
		{
			if (v.vesselType == VesselType.Debris) return false;
			return isVesselKnown(v.id);
		}

		internal SCANsensor getSensorStatus(Vessel v, SCANtype sensor)
		{
			if (!knownVessels.ContainsKey(v.id)) return null;
			if (!knownVessels[v.id].sensors.ContainsKey(sensor)) return null;
			return knownVessels[v.id].sensors[sensor];
		}

		internal SCANtype activeSensorsOnVessel(Guid id)
		{
			if (!knownVessels.ContainsKey(id)) return SCANtype.Nothing;
			SCANtype sensors = SCANtype.Nothing;
			foreach (SCANtype s in knownVessels[id].sensors.Keys) sensors |= s;
			return sensors;
		}

		private int i = 0;
		private static int last_scan_frame;
		private static float last_scan_time;
		private static double scan_UT;
		private int activeSensors, activeVessels;
		private static int currentActiveSensor, currentActiveVessel;
		private void scanFromAllVessels()
		{
			if (Time.realtimeSinceStartup - last_scan_time < 1 && Time.realtimeSinceStartup > last_scan_time) return;
			if (last_scan_frame == Time.frameCount) return;
			last_scan_frame = Time.frameCount;
			last_scan_time = Time.realtimeSinceStartup;
			scan_UT = Planetarium.GetUniversalTime();
			currentActiveSensor = 0;
			currentActiveVessel = 0;
			actualPasses = 0;
			maxRes = 0;
			if (body_data.Count > 0)
			{
				var bdata = body_data.ElementAt(i);     //SCANUtil.getData(FlightGlobals.Bodies[i]); //Update coverage for planets one at a time, rather than all together
				bdata.Value.updateCoverage();
				i++;
				if (i >= body_data.Count) i = 0;
			}
			foreach (Vessel v in FlightGlobals.Vessels)
			{
				if (!knownVessels.ContainsKey(v.id)) continue;
				SCANvessel vessel = knownVessels[v.id];
				SCANdata data = SCANUtil.getData(v.mainBody);
				if (data == null)
					continue;
				vessel.vessel = v;

				if (!data.Disabled)
				{
					if (v.mainBody == FlightGlobals.currentMainBody || scan_background)
					{
						if (isVesselKnown(v))
						{
							doScanPass(knownVessels[v.id], scan_UT, scan_UT, vessel.lastUT, vessel.latitude, vessel.longitude);
							++currentActiveVessel;
							currentActiveSensor += knownVessels[v.id].sensors.Count;
						}
					}
				}

				vessel.body = v.mainBody;
				vessel.frame = Time.frameCount;
				vessel.lastUT = scan_UT;
				vessel.latitude = SCANUtil.fixLatShift(v.latitude);
				vessel.longitude = SCANUtil.fixLonShift(v.longitude);
			}
			activeVessels = currentActiveVessel;
			activeSensors = currentActiveSensor;
		}

		private int actualPasses, maxRes;
		private static Queue<double> scanQueue;
		private void doScanPass(SCANvessel vessel, double UT, double startUT, double lastUT, double llat, double llon)
		{
			Vessel v = vessel.vessel;
			SCANdata data = SCANUtil.getData(v.mainBody);
			if (data == null)
				return;
			double soi_radius = v.mainBody.sphereOfInfluence - v.mainBody.Radius;
			double alt = v.altitude, lat = SCANUtil.fixLatShift(v.latitude), lon = SCANUtil.fixLonShift(v.longitude);
			double res = 0;
			Orbit o = v.orbit;
			bool uncovered;

			if (scanQueue == null) scanQueue = new Queue<double>();
			if (scanQueue.Count != 0) scanQueue.Clear();

		loop: // don't look at me like that, I just unrolled the recursion
			if (res > 0)
			{
				if (double.IsNaN(UT)) goto dequeue;
				if (o.ObT <= 0) goto dequeue;
				if (double.IsNaN(o.getObtAtUT(UT))) goto dequeue;
				Vector3d pos = o.getPositionAtUT(UT);
				double rotation = 0;
				if (v.mainBody.rotates)
				{
					rotation = (360 * ((UT - scan_UT) / v.mainBody.rotationPeriod)) % 360;
				}
				alt = v.mainBody.GetAltitude(pos);
				lat = SCANUtil.fixLatShift(v.mainBody.GetLatitude(pos));
				lon = SCANUtil.fixLonShift(v.mainBody.GetLongitude(pos) - rotation);
				if (alt < 0) alt = 0;
				if (res > maxRes) maxRes = (int)res;
			}
			else
			{
				alt = v.heightFromTerrain;
				if (alt < 0) alt = v.altitude;
			}

			if (Math.Abs(lat - llat) < 1 && Math.Abs(lon - llon) < 1 && res > 0) goto dequeue;
			actualPasses++;

			uncovered = res <= 0;
			foreach (SCANsensor sensor in knownVessels[v.id].sensors.Values)
			{
				if (res <= 0)
				{
					if (data.getCoverage(sensor.sensor) > 0) uncovered = false;
				}

				sensor.inRange = false;
				sensor.bestRange = false;
				if (alt < sensor.min_alt) continue;
				if (alt > Math.Min(sensor.max_alt, soi_radius)) continue;
				sensor.inRange = true;

				double fov = sensor.fov;
				double ba = Math.Min(sensor.best_alt, soi_radius);
				if (alt < ba) fov = (alt / ba) * fov;
				else sensor.bestRange = true;

				double surfscale = 600000d / v.mainBody.Radius;
				if (surfscale < 1) surfscale = 1;
				surfscale = Math.Sqrt(surfscale);
				fov *= surfscale;
				if (fov > 20) fov = 20;

				int f = (int)Math.Truncate(fov);
				int f1 = f + (int)Math.Round(fov - f);

				double clampLat;
				double clampLon;
				for (int x = -f; x <= f1; ++x)
				{
					clampLon = lon + x;	// longitude does not need clamping
					/*if (clampLon < 0  ) clampLon = 0; */
					/*if (clampLon > 360) clampLon = 360; */
					for (int y = -f; y <= f1; ++y)
					{
						clampLat = lat + y;
						if (clampLat > 90) clampLat = 90;
						if (clampLat < -90) clampLat = -90;
						SCANUtil.registerPass(clampLon, clampLat, data, sensor.sensor);
					}
				}
			}
			if (uncovered) return;
			/* 
			if(v.mainBody == FlightGlobals.currentMainBody) {
				if(res > 0) data.map_small.SetPixel((int)Math.Round(lon) + 180, (int)Math.Round(lat) + 90, palette.magenta);
				else data.map_small.SetPixel((int)Math.Round(lon) + 180, (int)Math.Round(lat) + 90, palette.yellow);
			}
			*/

			if (vessel.lastUT <= 0) return;
			if (vessel.frame <= 0) return;
			if (v.LandedOrSplashed) return;
			if (res >= timeWarpResolution) goto dequeue;

			if (startUT > UT)
			{
				scanQueue.Enqueue((startUT + UT) / 2);
				scanQueue.Enqueue(startUT);
				scanQueue.Enqueue(UT);
				scanQueue.Enqueue(lat);
				scanQueue.Enqueue(lon);
				scanQueue.Enqueue(res + 1);
			}
			startUT = UT;
			UT = (lastUT + UT) / 2;
			llat = lat;
			llon = lon;
			res = res + 1;
			goto loop;

		dequeue:
			if (scanQueue.Count <= 0) return;
			UT = scanQueue.Dequeue();
			startUT = scanQueue.Dequeue();
			lastUT = scanQueue.Dequeue();
			llat = scanQueue.Dequeue();
			llon = scanQueue.Dequeue();
			res = scanQueue.Dequeue();
			goto loop;
		}

	}
}
