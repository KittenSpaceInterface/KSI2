using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml.Serialization;
using Brutal;
using Brutal.ImGuiApi.Abstractions;
using Brutal.Logging;
using Brutal.Logging.Internal;
using Brutal.Numerics;
using Brutal.VulkanApi.Abstractions;
using Core;
using KSA.Rendering.Particles;
using Tomlet;

namespace KSA;

public static class ModLibrary
{
	public interface IKeyedLookup<out T> where T : IKeyed
	{
		static abstract T? FindById(string id);
	}

	private struct FileRef
	{
		public readonly FileInfo File;

		public readonly ByteReference Size;

		public readonly string Path;

		public FileRef(string filePath)
		{
			Path = filePath;
			filePath = System.IO.Path.Combine("Content", filePath);
			File = new FileInfo(filePath);
			Size = new ByteReference(Mod.Empty, File.Length);
		}
	}

	public enum TextureAction
	{
		[XmlEnum("list")]
		List,
		[XmlEnum("summarize")]
		Summarize
	}

	private class SizeResult
	{
		public ElementCount Count;

		public ByteSize Size;
	}

	public static List<string> OnBoot = new List<string>();

	public static List<string> OnLoad = new List<string>();

	internal static readonly SerializedCollection<Mod> Lookup = new SerializedCollection<Mod>("mods");

	internal static readonly SerializedCollection<FileReference> AllFiles = new SerializedCollection<FileReference>("files");

	internal static readonly SerializedCollection<PbrMaterialReference> AllMaterials = new SerializedCollection<PbrMaterialReference>("materials");

	internal static readonly SerializedCollection<GaugeComponent> AllGaugeComponents = new SerializedCollection<GaugeComponent>("gauge components");

	internal static readonly SerializedCollection<GaugeCanvas> AllGaugeCanvases = new SerializedCollection<GaugeCanvas>("gauge canvases");

	internal static readonly SerializedCollection<Gltf2Reference> AllGltfs = new SerializedCollection<Gltf2Reference>("gltfs");

	internal static readonly SerializedCollection<PartGameDataReference> AllPartGameDataReferences = new SerializedCollection<PartGameDataReference>("Part game data");

	internal static readonly SerializedCollection<MeshReference> AllMeshes = new SerializedCollection<MeshReference>("meshes");

	internal static readonly SerializedCollection<LanguageFileReference> AllLanguageFiles = new SerializedCollection<LanguageFileReference>("language files");

	internal static readonly SerializedCollection<ShaderReference> AllShaders = new SerializedCollection<ShaderReference>("Shaders");

	internal static readonly SerializedCollection<PartTemplate> AllParts = new SerializedCollection<PartTemplate>("Part templates");

	internal static readonly SerializedCollection<ClutterObjectTemplate> AllClutterObjects = new SerializedCollection<ClutterObjectTemplate>("clutter objects");

	internal static readonly SerializedCollection<ClutterObjectGameDataReference> AllClutterObjectGameData = new SerializedCollection<ClutterObjectGameDataReference>("clutter object game data");

	internal static readonly SerializedCollection<StaticObjectTemplate> AllStaticObjects = new SerializedCollection<StaticObjectTemplate>("static objects");

	internal static readonly SerializedCollection<StaticSubObjectTemplate> AllStaticSubObjects = new SerializedCollection<StaticSubObjectTemplate>("static sub-objects");

	internal static readonly SerializedCollection<StaticObjectGameDataReference> AllStaticObjectGameData = new SerializedCollection<StaticObjectGameDataReference>("static object game data");

	internal static readonly SerializedCollection<GimbalReference> AllGimbals = new SerializedCollection<GimbalReference>("Gimbal references");

	internal static readonly SerializedCollection<CharacterReference> AllCharacters = new SerializedCollection<CharacterReference>("characters");

	internal static readonly SerializedCollection<CharacterCoreReference> AllCharacterCores = new SerializedCollection<CharacterCoreReference>("character cores");

	internal static readonly SerializedCollection<CharacterFurReference> AllCharacterFurs = new SerializedCollection<CharacterFurReference>("character furs");

	internal static readonly SerializedCollection<CharacterExpressionsReference> AllCharacterExpressions = new SerializedCollection<CharacterExpressionsReference>("character expressions");

	internal static readonly SerializedCollection<CharacterMMUAnimationsReference> AllCharacterMMUAnimations = new SerializedCollection<CharacterMMUAnimationsReference>("character mmu animations");

	internal static readonly SerializedCollection<CharacterAttachmentReference> AllCharacterAttachments = new SerializedCollection<CharacterAttachmentReference>("character attachments");

	internal static readonly SerializedCollection<CharacterAnimationMasksReference> AllCharacterAnimationMasks = new SerializedCollection<CharacterAnimationMasksReference>("character animation masks");

	internal static readonly SerializedCollection<CharacterGroundAnimationsReference> AllCharacterGroundAnimations = new SerializedCollection<CharacterGroundAnimationsReference>("character ground animations");

	internal static readonly SerializedCollection<CharacterTexturesReference> AllCharacterTextures = new SerializedCollection<CharacterTexturesReference>("character textures");

	internal static readonly SerializedCollection<SoundBehavior> AllSoundBehaviours = new SerializedCollection<SoundBehavior>("sound behaviours");

	internal static readonly SerializedCollection<SpatialSoundData> AllSpatialSoundData = new SerializedCollection<SpatialSoundData>("spatial soundData");

	internal static readonly SerializedCollection<SoundGroupData> AllSoundGroups = new SerializedCollection<SoundGroupData>("sound groups");

	internal static readonly SerializedCollection<MusicPlayList> AllMusicPlaylists = new SerializedCollection<MusicPlayList>("music playlists");

	internal static readonly SerializedCollection<ChannelGroupReference> AllChannelGroups = new SerializedCollection<ChannelGroupReference>("channel groups");

	internal static readonly SerializedCollection<DspReference> AllDsp = new SerializedCollection<DspReference>("all dsp");

	internal static readonly SerializedCollection<SubstanceTemplate> AllSubstances = new SerializedCollection<SubstanceTemplate>("substances");

	internal static readonly SerializedCollection<ReactionTemplate> AllReactions = new SerializedCollection<ReactionTemplate>("reactions");

	internal static readonly SerializedCollection<GrainGeometryTemplate> AllGrainGeometries = new SerializedCollection<GrainGeometryTemplate>("grain geometries");

	internal static readonly SerializedCollection<SituationTemplate> AllSituations = new SerializedCollection<SituationTemplate>("situations");

	internal static readonly SerializedCollection<ParticleEmitterReference> AllParticleEmitters = new SerializedCollection<ParticleEmitterReference>("particle emitters");

	internal static readonly SerializedCollection<ParticleMeshReference> AllParticleMeshes = new SerializedCollection<ParticleMeshReference>("particle meshes");

	internal static readonly SerializedCollection<ExplosionReference> AllExplosions = new SerializedCollection<ExplosionReference>("explosions");

	internal static readonly SerializedCollection<DistantGlintReference> AllDistantGlints = new SerializedCollection<DistantGlintReference>("distant glints");

	internal static readonly SerializedCollection<EditorTagDefinition> AllEditorTagDefinitions = new SerializedCollection<EditorTagDefinition>("editor tag definitions");

	public const string MOD_TOML = "mod.toml";

	public const string CONTENT_FOLDER = "Content";

	private const string CONTENT_MANIFEST = "manifest.toml";

	private const string MODS_FOLDER = "mods";

	public static List<ILoader> Loaders = new List<ILoader>();

	public static List<IBinder> Binders = new List<IBinder>();

	public static ModManifest Manifest = null;

	private static bool _manifestPrepared = false;

	private static readonly Func<FileReference, bool> CacheSize = (FileReference file) =>
	{
		if (file.IsReference())
		{
			return false;
		}
		file.Size = file.GetSize();
		return true;
	};

	internal static readonly SerializedCollection<AstronomicalTemplate> TemplateLookup = new SerializedCollection<AstronomicalTemplate>("astronomical template");

	private static readonly StringEnumCollection<TextureAction> _textureActions = new StringEnumCollection<TextureAction>(toProper: false);

	public static string LocalModsFolderPath => Path.Combine(Constants.DocumentsFolderPath, "mods");

	public static string LocalManifestPath => Path.Combine(Constants.DocumentsFolderPath, "manifest.toml");

	public static Mod? Find(KeyHash hash)
	{
		return Lookup.Find(hash);
	}

	public static Mod? Find(string id)
	{
		return Find(KeyHash.Make(id));
	}

	public static void RegisterLoader(ILoader loader)
	{
		if (loader is LanguageFileReference)
		{
			return;
		}
		lock (Loaders)
		{
			Loaders.Add(loader);
		}
	}

	public static void OnApplicationStart()
	{
		CheckDirectories();
	}

	private static void CheckDirectories()
	{
		if (!Directory.Exists(Constants.DocumentsFolderPath))
		{
			Directory.CreateDirectory(Constants.DocumentsFolderPath);
		}
		if (!Directory.Exists(LocalModsFolderPath))
		{
			Directory.CreateDirectory(LocalModsFolderPath);
		}
	}

	public static void RegisterBinder(IBinder binder)
	{
		lock (Binders)
		{
			Binders.Add(binder);
		}
	}

	public static string GetAppPath(string localPath)
	{
		return Path.Combine("Content", localPath);
	}

	public static bool PrepareManifest()
	{
		if (_manifestPrepared)
		{
			return true;
		}
		string text = Path.Combine("Content");
		bool shouldAppend;
		if (!Directory.Exists(text))
		{
			LogCategory log = DefaultCategory.Log;
			LogCategory logCategory = log;
			LogString<Error> message = new LogString<Error>(30, 1, log, out shouldAppend);
			if (shouldAppend)
			{
				message.AppendLiteral("Content directory '");
				message.AppendFormatted(text);
				message.AppendLiteral("' not found");
			}
			logCategory.Error(message, "PrepareManifest", "C:\\prototype-planet\\KSA\\ModLibrary.cs", 1405);
			return false;
		}
		if (!File.Exists(LocalManifestPath))
		{
			File.Copy(Path.Combine("Content", "manifest.toml"), LocalManifestPath, overwrite: true);
		}
		if (!File.Exists(LocalManifestPath))
		{
			LogCategory log2 = DefaultCategory.Log;
			LogCategory logCategory2 = log2;
			LogString<Error> message = new LogString<Error>(26, 1, log2, out shouldAppend);
			if (shouldAppend)
			{
				message.AppendLiteral("Manifest file '");
				message.AppendFormatted(LocalManifestPath);
				message.AppendLiteral("' not found");
			}
			logCategory2.Error(message, "PrepareManifest", "C:\\prototype-planet\\KSA\\ModLibrary.cs", 1414);
			return false;
		}
		Manifest = TomletMain.To<ModManifest>(File.ReadAllText(LocalManifestPath));
		string text2 = Path.Combine(text, "manifest.toml");
		ModManifest sourceManifest = TomletMain.To<ModManifest>(File.ReadAllText(text2));
		List<ModEntry> list = sourceManifest.Mods.Where((ModEntry entry) => Manifest.Mods.All((ModEntry m) => m.Id != entry.Id)).ToList();
		if (list.Count > 0)
		{
			LogCategory log3 = DefaultCategory.Log;
			LogCategory logCategory3 = log3;
			LogString<Warning> message2 = new LogString<Warning>(26, 2, log3, out shouldAppend);
			if (shouldAppend)
			{
				message2.AppendLiteral("Found ");
				message2.AppendFormatted(list.Count);
				message2.AppendLiteral(" orphaned mods in '");
				message2.AppendFormatted(text2);
				message2.AppendLiteral("'");
			}
			logCategory3.Warning(message2, "PrepareManifest", "C:\\prototype-planet\\KSA\\ModLibrary.cs", 1432);
			Manifest.Mods.AddRange(list);
			Manifest.Save();
		}
		List<ModEntry> list2 = Manifest.Mods.Where((ModEntry entry) => sourceManifest.Mods.Any((ModEntry m) => m.Id == entry.Id)).ToList();
		foreach (ModEntry item in list2)
		{
			item.Core = true;
		}
		AddMods(text);
		if (Directory.Exists(LocalModsFolderPath))
		{
			AddMods(LocalModsFolderPath);
		}
		List<ModEntry> list3 = Manifest.Mods.Where((ModEntry entry) => !entry.Core).ToList();
		foreach (ModEntry item2 in list3)
		{
			if (!item2.Exists())
			{
				LogCategory log4 = DefaultCategory.Log;
				LogCategory logCategory4 = log4;
				LogString<Warning> message2 = new LogString<Warning>(37, 1, log4, out shouldAppend);
				if (shouldAppend)
				{
					message2.AppendLiteral("Removing missing mod '");
					message2.AppendFormatted(item2.Id);
					message2.AppendLiteral("' from manifest");
				}
				logCategory4.Warning(message2, "PrepareManifest", "C:\\prototype-planet\\KSA\\ModLibrary.cs", 1455);
				Manifest.Mods.Remove(item2);
				Manifest.Save();
			}
		}
		_manifestPrepared = true;
		return true;
	}

	private static void AddMods(string contentPath)
	{
		List<DirectoryInfo> list = (from path in Directory.GetDirectories(contentPath)
			select new DirectoryInfo(path) into dir
			where File.Exists(Path.Combine(dir.FullName, "mod.toml"))
			select dir).ToList();
		if (list.Count <= 0)
		{
			return;
		}
		foreach (DirectoryInfo modDir in list)
		{
			string text = Path.Combine(modDir.FullName, "mod.toml");
			bool shouldAppend;
			if (!File.Exists(text))
			{
				LogCategory log = DefaultCategory.Log;
				LogCategory logCategory = log;
				LogString<Error> message = new LogString<Error>(32, 1, log, out shouldAppend);
				if (shouldAppend)
				{
					message.AppendLiteral("Mod definition file '");
					message.AppendFormatted(text);
					message.AppendLiteral("' not found");
				}
				logCategory.Error(message, "AddMods", "C:\\prototype-planet\\KSA\\ModLibrary.cs", 1477);
			}
			else if (Manifest.Mods.Count((ModEntry entry) => entry.Id == modDir.Name) <= 0)
			{
				ModEntry modEntry = new ModEntry(modDir.Name, Manifest.Mods.Count);
				Manifest.Mods.Add(modEntry);
				LogCategory log2 = DefaultCategory.Log;
				LogCategory logCategory2 = log2;
				LogString<Warning> message2 = new LogString<Warning>(27, 1, log2, out shouldAppend);
				if (shouldAppend)
				{
					message2.AppendLiteral("Found local orphaned mod '");
					message2.AppendFormatted(modEntry.Id);
					message2.AppendLiteral("'");
				}
				logCategory2.Warning(message2, "AddMods", "C:\\prototype-planet\\KSA\\ModLibrary.cs", 1484);
			}
		}
	}

	public static void PrepareAll()
	{
		string path = Path.Combine("Content");
		if (!PrepareManifest())
		{
			return;
		}
		for (int i = 0; i < Manifest.Mods.Count; i++)
		{
			ModEntry modEntry = Manifest.Mods[i];
			if (!modEntry.Enabled)
			{
				continue;
			}
			modEntry.State = true;
			modEntry.Index = i;
			string text = Path.Combine(path, modEntry.Id);
			bool shouldAppend;
			if (!Directory.Exists(text))
			{
				text = Path.Combine(LocalModsFolderPath, modEntry.Id);
				if (!Directory.Exists(text))
				{
					LogCategory log = DefaultCategory.Log;
					LogCategory logCategory = log;
					LogString<Error> message = new LogString<Error>(26, 1, log, out shouldAppend);
					if (shouldAppend)
					{
						message.AppendLiteral("Mod directory '");
						message.AppendFormatted(text);
						message.AppendLiteral("' not found");
					}
					logCategory.Error(message, "PrepareAll", "C:\\prototype-planet\\KSA\\ModLibrary.cs", 1509);
					continue;
				}
			}
			string text2 = Path.Combine(text, "mod.toml");
			if (!File.Exists(text2))
			{
				text = Path.Combine(LocalModsFolderPath, modEntry.Id);
				text2 = Path.Combine(text, "mod.toml");
				if (!File.Exists(text2))
				{
					LogCategory log2 = DefaultCategory.Log;
					LogCategory logCategory2 = log2;
					LogString<Error> message = new LogString<Error>(32, 1, log2, out shouldAppend);
					if (shouldAppend)
					{
						message.AppendLiteral("Mod definition file '");
						message.AppendFormatted(text2);
						message.AppendLiteral("' not found");
					}
					logCategory2.Error(message, "PrepareAll", "C:\\prototype-planet\\KSA\\ModLibrary.cs", 1521);
					continue;
				}
			}
			Mod mod = Mod.MakeUsing(modEntry.Id, text2);
			if (mod.IsValid())
			{
				LogCategory log3 = DefaultCategory.Log;
				LogCategory logCategory3 = log3;
				LogString<Information> message2 = new LogString<Information>(12, 1, log3, out shouldAppend);
				if (shouldAppend)
				{
					message2.AppendLiteral("found mod '");
					message2.AppendFormatted(mod.Id);
					message2.AppendLiteral("'");
				}
				logCategory3.Info(message2, "PrepareAll", "C:\\prototype-planet\\KSA\\ModLibrary.cs", 1529);
				mod.RegisterTo(Lookup);
				mod.PrepareSystems();
				mod.LoadSettings();
			}
		}
	}

	public static void PreloadAssetBundles()
	{
		if (!PrepareManifest())
		{
			return;
		}
		foreach (Mod item in Lookup.GetList())
		{
			Loading.State("Pre-Loading " + item.Id);
			item.Preload = true;
			item.LoadAssetBundles();
			item.LoadSystems();
		}
	}

	public static void LoadEditorTags()
	{
		if (!PrepareManifest())
		{
			return;
		}
		foreach (Mod item in Lookup.GetList())
		{
			Loading.State("Loading editor tags from " + item.Id);
			item.LoadEditorTagDefinitions();
		}
		VehicleEditor.MarkEditorTagDefinitionsLoaded();
	}

	public static void LoadAll()
	{
		if (!PrepareManifest())
		{
			return;
		}
		foreach (Mod item in Lookup.GetList())
		{
			item.Preload = false;
			item.AssetBundles.Clear();
		}
		SystemLibrary.Lookup = new SerializedCollection<SystemTemplate>("system templates");
		foreach (Mod item2 in Lookup.GetList())
		{
			Loading.State("Loading " + item2.Id);
			item2.LoadFonts();
			item2.LoadAssetBundles();
			item2.LoadSystems();
			item2.LoadPlanetMeshes();
			item2.LoadPlanetRandomHeightmaps();
			foreach (string item3 in item2.Console.OnLoad)
			{
				OnLoad.Add(item3);
			}
			foreach (string item4 in item2.Console.OnBoot)
			{
				OnBoot.Add(item4);
			}
		}
		Task task;
		lock (Loaders)
		{
			task = Parallel.ForEachAsync(Loaders, (ILoader loader, CancellationToken token) =>
			{
				loader.Load();
				return ValueTask.CompletedTask;
			});
		}
		while (!task.Wait(1))
		{
			Loading.Current?.OnFrame();
		}
		OnDataLoaded();
		Universe.OnDataLoaded();
	}

	public static void OnDataLoaded()
	{
		AllFiles.ForEach(CacheSize);
		Comparison<FileReference> comparison = (FileReference x, FileReference y) =>
		{
			if (x.Size < y.Size)
			{
				return 1;
			}
			return (x.Size > y.Size) ? (-1) : 0;
		};
		AllFiles.SortList(comparison);
	}

	public static void AssignDefaults()
	{
		TextureReference.EmptyWhite = Get<TextureReference>("EmptyWhite");
		TextureReference.EmptyBlack = Get<TextureReference>("EmptyBlack");
		TextureReference.EmptyNormal = Get<TextureReference>("EmptyNormal");
		TextureReference.CubemapTemplate = Get<TextureReference>("CubemapTemplate");
		TextureReference.EmptyCubemap = Get<TextureReference>("EmptyCubemap");
		TextureReference.EmptyNormalCubemap = Get<TextureReference>("EmptyNormalCubemap");
		PlanetMeshCollection.Default = PlanetMeshLibrary.Find("Default") ?? throw new NullReferenceException("Default planet mesh collection not found");
		HapkeSurgeReference.Default = Get<TextureReference>("DefaultHapkeSurge");
		HapkeScatteringReference.Default = Get<TextureReference>("DefaultHapkeScattering");
	}

	public static int GetStarCount()
	{
		int num = 200;
		foreach (Mod item in Lookup.GetList())
		{
			num += item.GetStarCount();
		}
		return num;
	}

	public static void PopulateStars(InstancedStarTechnique starTechnique)
	{
		foreach (Mod item in Lookup.GetList())
		{
			item.LoadStarBinaries(starTechnique);
		}
		ReadOnlySpan<IViewport> views = ViewportRegistry.Views;
		for (int i = 0; i < views.Length; i++)
		{
			IViewport viewport = views[i];
			starTechnique.UpdateInstanceBuffer(viewport, 0);
		}
	}

	public static bool Has<T>(string id) where T : IKeyed
	{
		T value;
		return TryGet<T>(id, out value);
	}

	public static bool Register(FileReference modFile)
	{
		return AllFiles.Register(modFile);
	}

	public static bool Register(MeshReference mesh)
	{
		return AllMeshes.Register(mesh);
	}

	public static bool Register(LanguageFileReference languageFile)
	{
		return AllLanguageFiles.Register(languageFile);
	}

	public static bool Register(ShaderReference shader)
	{
		return AllShaders.Register(shader);
	}

	public static bool Register(SoundBehavior soundBehavior)
	{
		return AllSoundBehaviours.Register(soundBehavior);
	}

	public static bool Register(PbrMaterialReference modFile)
	{
		return AllMaterials.Register(modFile);
	}

	public static bool Register(Gltf2Reference mesh)
	{
		return AllGltfs.Register(mesh);
	}

	public static bool Register(CharacterReference modFile)
	{
		return AllCharacters.Register(modFile);
	}

	public static bool Register(CharacterCoreReference modFile)
	{
		return AllCharacterCores.Register(modFile);
	}

	public static bool Register(CharacterAttachmentReference modFile)
	{
		return AllCharacterAttachments.Register(modFile);
	}

	public static bool Register(CharacterFurReference modFile)
	{
		return AllCharacterFurs.Register(modFile);
	}

	public static bool Register(CharacterExpressionsReference modFile)
	{
		return AllCharacterExpressions.Register(modFile);
	}

	public static bool Register(CharacterMMUAnimationsReference modFile)
	{
		return AllCharacterMMUAnimations.Register(modFile);
	}

	public static bool Register(CharacterGroundAnimationsReference modFile)
	{
		return AllCharacterGroundAnimations.Register(modFile);
	}

	public static bool Register(CharacterAnimationMasksReference modFile)
	{
		return AllCharacterAnimationMasks.Register(modFile);
	}

	public static bool Register(CharacterTexturesReference modFile)
	{
		return AllCharacterTextures.Register(modFile);
	}

	public static bool Register(GaugeCanvas modFile)
	{
		return AllGaugeCanvases.Register(modFile);
	}

	public static bool Register(GaugeComponent modFile)
	{
		return AllGaugeComponents.Register(modFile);
	}

	public static bool Register(PartTemplate template)
	{
		return AllParts.Register(template);
	}

	public static bool Register(ClutterObjectTemplate template)
	{
		return AllClutterObjects.Register(template);
	}

	public static bool Register(ClutterObjectGameDataReference reference)
	{
		return AllClutterObjectGameData.Register(reference);
	}

	public static bool Register(StaticObjectTemplate template)
	{
		return AllStaticObjects.Register(template);
	}

	public static bool Register(StaticSubObjectTemplate template)
	{
		return AllStaticSubObjects.Register(template);
	}

	public static bool Register(StaticObjectGameDataReference reference)
	{
		return AllStaticObjectGameData.Register(reference);
	}

	public static bool Register(GimbalReference reference)
	{
		return AllGimbals.Register(reference);
	}

	public static bool Register(PartGameDataReference reference)
	{
		return AllPartGameDataReferences.Register(reference);
	}

	public static bool Register(SpatialSoundData spatialSoundData)
	{
		return AllSpatialSoundData.Register(spatialSoundData);
	}

	public static bool Register(SoundGroupData soundGroupData)
	{
		return AllSoundGroups.Register(soundGroupData);
	}

	public static bool Register(MusicPlayList musicPlaylist)
	{
		return AllMusicPlaylists.Register(musicPlaylist);
	}

	public static bool Register(SubstanceTemplate substance)
	{
		return AllSubstances.Register(substance);
	}

	public static bool Register(ReactionTemplate reaction)
	{
		return AllReactions.Register(reaction);
	}

	public static bool Register(GrainGeometryTemplate grainGeometry)
	{
		return AllGrainGeometries.Register(grainGeometry);
	}

	public static bool Register(SituationTemplate situation)
	{
		return AllSituations.Register(situation);
	}

	public static bool Register(ParticleEmitterReference emitter)
	{
		return AllParticleEmitters.Register(emitter);
	}

	public static bool Register(ExplosionReference explosion)
	{
		return AllExplosions.Register(explosion);
	}

	public static bool Register(DistantGlintReference glint)
	{
		return AllDistantGlints.Register(glint);
	}

	public static bool Register(ChannelGroupReference channelGroupReference)
	{
		return AllChannelGroups.Register(channelGroupReference);
	}

	public static bool Register(EditorTagDefinition editorTagDefinition)
	{
		return AllEditorTagDefinitions.Register(editorTagDefinition);
	}

	public static bool Register(DspReference dspReference)
	{
		return AllDsp.Register(dspReference);
	}

	public static bool TryGet<T>(string id, out T? value) where T : IKeyed
	{
		Type typeFromHandle = typeof(T);
		if (typeFromHandle == typeof(FileReference) || typeFromHandle.IsSubclassOf(typeof(FileReference)))
		{
			if (!(AllFiles.Find(KeyHash.Make(id)) is T val) || 1 == 0)
			{
				value = default;
				return false;
			}
			value = val;
			return true;
		}
		if (typeFromHandle == typeof(SoundBehavior) || typeFromHandle.IsSubclassOf(typeof(SoundBehavior)))
		{
			KeyHash hash = KeyHash.Make(id);
			if (AllSoundBehaviours.Find(hash) is T val2)
			{
				value = val2;
				return true;
			}
			value = default;
			return false;
		}
		if (typeFromHandle == typeof(PbrMaterialReference) || typeFromHandle.IsSubclassOf(typeof(PbrMaterialReference)))
		{
			if (!(AllMaterials.Find(KeyHash.Make(id)) is T val3) || 1 == 0)
			{
				value = default;
				return false;
			}
			value = val3;
			return true;
		}
		if (typeFromHandle == typeof(GaugeCanvas) || typeFromHandle.IsSubclassOf(typeof(GaugeCanvas)))
		{
			if (!(AllGaugeCanvases.Find(KeyHash.Make(id)) is T val4) || 1 == 0)
			{
				value = default;
				return false;
			}
			value = val4;
			return true;
		}
		if (typeFromHandle == typeof(GaugeComponent) || typeFromHandle.IsSubclassOf(typeof(GaugeComponent)))
		{
			if (!(AllGaugeComponents.Find(KeyHash.Make(id)) is T val5) || 1 == 0)
			{
				value = default;
				return false;
			}
			value = val5;
			return true;
		}
		if (typeFromHandle == typeof(AstronomicalTemplate) || typeFromHandle.IsSubclassOf(typeof(AstronomicalTemplate)))
		{
			if (!(TemplateLookup.Find(KeyHash.Make(id)) is T val6) || 1 == 0)
			{
				value = default;
				return false;
			}
			value = val6;
			return true;
		}
		if (typeFromHandle == typeof(SpatialSoundData) || typeFromHandle.IsSubclassOf(typeof(SpatialSoundData)))
		{
			if (!(AllSpatialSoundData.Find(KeyHash.Make(id)) is T val7) || 1 == 0)
			{
				value = default;
				return false;
			}
			value = val7;
			return true;
		}
		if (typeFromHandle == typeof(SoundGroupData) || typeFromHandle.IsSubclassOf(typeof(SoundGroupData)))
		{
			if (!(AllSoundGroups.Find(KeyHash.Make(id)) is T val8) || 1 == 0)
			{
				value = default;
				return false;
			}
			value = val8;
			return true;
		}
		if (typeFromHandle == typeof(CharacterReference))
		{
			if (!(AllCharacters.Find(KeyHash.Make(id)) is T val9) || 1 == 0)
			{
				value = default;
				return false;
			}
			value = val9;
			return true;
		}
		if (typeFromHandle == typeof(CharacterCoreReference))
		{
			if (!(AllCharacterCores.Find(KeyHash.Make(id)) is T val10) || 1 == 0)
			{
				value = default;
				return false;
			}
			value = val10;
			return true;
		}
		if (typeFromHandle == typeof(CharacterAttachmentReference))
		{
			if (!(AllCharacterAttachments.Find(KeyHash.Make(id)) is T val11) || 1 == 0)
			{
				value = default;
				return false;
			}
			value = val11;
			return true;
		}
		if (typeFromHandle == typeof(CharacterFurReference))
		{
			if (!(AllCharacterFurs.Find(KeyHash.Make(id)) is T val12) || 1 == 0)
			{
				value = default;
				return false;
			}
			value = val12;
			return true;
		}
		if (typeFromHandle == typeof(CharacterExpressionsReference))
		{
			if (!(AllCharacterExpressions.Find(KeyHash.Make(id)) is T val13) || 1 == 0)
			{
				value = default;
				return false;
			}
			value = val13;
			return true;
		}
		if (typeFromHandle == typeof(CharacterMMUAnimationsReference))
		{
			if (!(AllCharacterMMUAnimations.Find(KeyHash.Make(id)) is T val14) || 1 == 0)
			{
				value = default;
				return false;
			}
			value = val14;
			return true;
		}
		if (typeFromHandle == typeof(CharacterAnimationMasksReference))
		{
			if (!(AllCharacterAnimationMasks.Find(KeyHash.Make(id)) is T val15) || 1 == 0)
			{
				value = default;
				return false;
			}
			value = val15;
			return true;
		}
		if (typeFromHandle == typeof(CharacterGroundAnimationsReference))
		{
			if (!(AllCharacterGroundAnimations.Find(KeyHash.Make(id)) is T val16) || 1 == 0)
			{
				value = default;
				return false;
			}
			value = val16;
			return true;
		}
		if (typeFromHandle == typeof(CharacterTexturesReference))
		{
			if (!(AllCharacterTextures.Find(KeyHash.Make(id)) is T val17) || 1 == 0)
			{
				value = default;
				return false;
			}
			value = val17;
			return true;
		}
		if (typeFromHandle == typeof(SituationTemplate))
		{
			if (!(AllSituations.Find(KeyHash.Make(id)) is T val18) || 1 == 0)
			{
				value = default;
				return false;
			}
			value = val18;
			return true;
		}
		if (typeFromHandle == typeof(ParticleEmitterReference))
		{
			if (!(AllParticleEmitters.Find(KeyHash.Make(id)) is T val19) || 1 == 0)
			{
				value = default;
				return false;
			}
			value = val19;
			return true;
		}
		if (typeFromHandle == typeof(ExplosionReference))
		{
			if (!(AllExplosions.Find(KeyHash.Make(id)) is T val20) || 1 == 0)
			{
				value = default;
				return false;
			}
			value = val20;
			return true;
		}
		if (typeFromHandle == typeof(DistantGlintReference) || typeFromHandle.IsSubclassOf(typeof(DistantGlintReference)))
		{
			if (!(AllDistantGlints.Find(KeyHash.Make(id)) is T val21) || 1 == 0)
			{
				value = default;
				return false;
			}
			value = val21;
			return true;
		}
		if (typeFromHandle == typeof(ChannelGroupReference) || typeFromHandle.IsSubclassOf(typeof(ChannelGroupReference)))
		{
			if (!(AllChannelGroups.Find(KeyHash.Make(id)) is T val22) || 1 == 0)
			{
				value = default;
				return false;
			}
			value = val22;
			return true;
		}
		if (typeFromHandle == typeof(DspReference) || typeFromHandle.IsSubclassOf(typeof(DspReference)))
		{
			if (!(AllDsp.Find(KeyHash.Make(id)) is T val23) || 1 == 0)
			{
				value = default;
				return false;
			}
			value = val23;
			return true;
		}
		if (typeFromHandle == typeof(ClutterObjectTemplate) || typeFromHandle.IsSubclassOf(typeof(ClutterObjectTemplate)))
		{
			if (!(AllClutterObjects.Find(KeyHash.Make(id)) is T val24) || 1 == 0)
			{
				value = default;
				return false;
			}
			value = val24;
			return true;
		}
		if (typeFromHandle == typeof(StaticObjectTemplate) || typeFromHandle.IsSubclassOf(typeof(StaticObjectTemplate)))
		{
			if (!(AllStaticObjects.Find(KeyHash.Make(id)) is T val25) || 1 == 0)
			{
				value = default;
				return false;
			}
			value = val25;
			return true;
		}
		if (typeFromHandle == typeof(StaticSubObjectTemplate) || typeFromHandle.IsSubclassOf(typeof(StaticSubObjectTemplate)))
		{
			if (!(AllStaticSubObjects.Find(KeyHash.Make(id)) is T val26) || 1 == 0)
			{
				value = default;
				return false;
			}
			value = val26;
			return true;
		}
		value = default;
		return false;
	}

	public static T[] Get<T>(params ReadOnlySpan<string> ids) where T : IKeyed
	{
		T[] array = new T[ids.Length];
		for (int i = 0; i < ids.Length; i++)
		{
			string text = ids[i];
			if (!TryGet<T>(text, out T value))
			{
				throw new NullReferenceException("No mod item found for '" + text + "'");
			}
			array[i] = value;
		}
		return array;
	}

	public static T Get<T>(string id) where T : IKeyed
	{
		Type typeFromHandle = typeof(T);
		if (typeFromHandle == typeof(FileReference) || typeFromHandle.IsSubclassOf(typeof(FileReference)))
		{
			if (!(AllFiles.Find(KeyHash.Make(id)) is T result) || 1 == 0)
			{
				throw new NullReferenceException(typeFromHandle.Name + " is null for '" + id + "'");
			}
			return result;
		}
		if (typeFromHandle == typeof(Gltf2Reference))
		{
			if (!(AllGltfs.Find(KeyHash.Make(id)) is T result2) || 1 == 0)
			{
				throw new NullReferenceException(typeFromHandle.Name + " is null for '" + id + "'");
			}
			return result2;
		}
		if (typeFromHandle == typeof(MeshReference) || typeFromHandle.IsSubclassOf(typeof(MeshReference)))
		{
			if (!(AllMeshes.Find(KeyHash.Make(id)) is T result3) || 1 == 0)
			{
				throw new NullReferenceException(typeFromHandle.Name + " is null for '" + id + "'");
			}
			return result3;
		}
		if (typeFromHandle == typeof(LanguageFileReference) || typeFromHandle.IsSubclassOf(typeof(LanguageFileReference)))
		{
			if (!(AllLanguageFiles.Find(KeyHash.Make(id)) is T result4) || 1 == 0)
			{
				throw new NullReferenceException(typeFromHandle.Name + " is null for '" + id + "'");
			}
			return result4;
		}
		if (typeFromHandle == typeof(ShaderReference) || typeFromHandle.IsSubclassOf(typeof(ShaderReference)))
		{
			if (!(AllShaders.Find(KeyHash.Make(id)) is T result5) || 1 == 0)
			{
				throw new NullReferenceException(typeFromHandle.Name + " is null for '" + id + "'");
			}
			return result5;
		}
		if (typeFromHandle == typeof(SoundBehavior) || typeFromHandle.IsSubclassOf(typeof(SoundBehavior)))
		{
			KeyHash hash = KeyHash.Make(id);
			if (!(AllSoundBehaviours.Find(hash) is T result6) || 1 == 0)
			{
				throw new NullReferenceException(typeFromHandle.Name + " is null for '" + id + "'");
			}
			return result6;
		}
		if (typeFromHandle == typeof(PbrMaterialReference) || typeFromHandle.IsSubclassOf(typeof(PbrMaterialReference)))
		{
			if (!(AllMaterials.Find(KeyHash.Make(id)) is T result7) || 1 == 0)
			{
				throw new NullReferenceException(typeFromHandle.Name + " is null for '" + id + "'");
			}
			return result7;
		}
		if (typeFromHandle == typeof(PartTemplate) || typeFromHandle.IsSubclassOf(typeof(PartTemplate)))
		{
			if (!(AllParts.Find(KeyHash.Make(id)) is T result8) || 1 == 0)
			{
				throw new NullReferenceException(typeFromHandle.Name + " is null for '" + id + "'");
			}
			return result8;
		}
		if (typeFromHandle == typeof(GimbalReference))
		{
			if (!(AllGimbals.Find(KeyHash.Make(id)) is T result9) || 1 == 0)
			{
				throw new NullReferenceException(typeFromHandle.Name + " is null for '" + id + "'");
			}
			return result9;
		}
		if (typeFromHandle == typeof(GaugeCanvas) || typeFromHandle.IsSubclassOf(typeof(GaugeCanvas)))
		{
			if (!(AllGaugeCanvases.Find(KeyHash.Make(id)) is T result10) || 1 == 0)
			{
				throw new NullReferenceException(typeFromHandle.Name + " is null for '" + id + "'");
			}
			return result10;
		}
		if (typeFromHandle == typeof(GaugeComponent) || typeFromHandle.IsSubclassOf(typeof(GaugeComponent)))
		{
			if (!(AllGaugeComponents.Find(KeyHash.Make(id)) is T result11) || 1 == 0)
			{
				throw new NullReferenceException(typeFromHandle.Name + " is null for '" + id + "'");
			}
			return result11;
		}
		if (typeFromHandle == typeof(AstronomicalTemplate) || typeFromHandle.IsSubclassOf(typeof(AstronomicalTemplate)))
		{
			if (!(TemplateLookup.Find(KeyHash.Make(id)) is T result12) || 1 == 0)
			{
				throw new NullReferenceException(typeFromHandle.Name + " is null for '" + id + "'");
			}
			return result12;
		}
		if (typeFromHandle == typeof(SpatialSoundData) || typeFromHandle.IsSubclassOf(typeof(SpatialSoundData)))
		{
			if (!(AllSpatialSoundData.Find(KeyHash.Make(id)) is T result13) || 1 == 0)
			{
				throw new NullReferenceException(typeFromHandle.Name + " is null for '" + id + "'");
			}
			return result13;
		}
		if (typeFromHandle == typeof(SoundGroupData) || typeFromHandle.IsSubclassOf(typeof(SoundGroupData)))
		{
			if (!(AllSoundGroups.Find(KeyHash.Make(id)) is T result14) || 1 == 0)
			{
				throw new NullReferenceException(typeFromHandle.Name + " is null for '" + id + "'");
			}
			return result14;
		}
		if (typeFromHandle == typeof(CharacterReference) || typeFromHandle.IsSubclassOf(typeof(CharacterReference)))
		{
			if (!(AllCharacters.Find(KeyHash.Make(id)) is T result15) || 1 == 0)
			{
				throw new NullReferenceException(typeFromHandle.Name + " is null for '" + id + "'");
			}
			return result15;
		}
		if (typeFromHandle == typeof(CharacterCoreReference) || typeFromHandle.IsSubclassOf(typeof(CharacterCoreReference)))
		{
			if (!(AllCharacterCores.Find(KeyHash.Make(id)) is T result16) || 1 == 0)
			{
				throw new NullReferenceException(typeFromHandle.Name + " is null for '" + id + "'");
			}
			return result16;
		}
		if (typeFromHandle == typeof(CharacterAttachmentReference) || typeFromHandle.IsSubclassOf(typeof(CharacterAttachmentReference)))
		{
			if (!(AllCharacterAttachments.Find(KeyHash.Make(id)) is T result17) || 1 == 0)
			{
				throw new NullReferenceException(typeFromHandle.Name + " is null for '" + id + "'");
			}
			return result17;
		}
		if (typeFromHandle == typeof(CharacterFurReference) || typeFromHandle.IsSubclassOf(typeof(CharacterFurReference)))
		{
			if (!(AllCharacterFurs.Find(KeyHash.Make(id)) is T result18) || 1 == 0)
			{
				throw new NullReferenceException(typeFromHandle.Name + " is null for '" + id + "'");
			}
			return result18;
		}
		if (typeFromHandle == typeof(CharacterExpressionsReference) || typeFromHandle.IsSubclassOf(typeof(CharacterExpressionsReference)))
		{
			if (!(AllCharacterExpressions.Find(KeyHash.Make(id)) is T result19) || 1 == 0)
			{
				throw new NullReferenceException(typeFromHandle.Name + " is null for '" + id + "'");
			}
			return result19;
		}
		if (typeFromHandle == typeof(CharacterMMUAnimationsReference) || typeFromHandle.IsSubclassOf(typeof(CharacterMMUAnimationsReference)))
		{
			if (!(AllCharacterMMUAnimations.Find(KeyHash.Make(id)) is T result20) || 1 == 0)
			{
				throw new NullReferenceException(typeFromHandle.Name + " is null for '" + id + "'");
			}
			return result20;
		}
		if (typeFromHandle == typeof(CharacterAnimationMasksReference) || typeFromHandle.IsSubclassOf(typeof(CharacterAnimationMasksReference)))
		{
			if (!(AllCharacterAnimationMasks.Find(KeyHash.Make(id)) is T result21) || 1 == 0)
			{
				throw new NullReferenceException(typeFromHandle.Name + " is null for '" + id + "'");
			}
			return result21;
		}
		if (typeFromHandle == typeof(CharacterGroundAnimationsReference) || typeFromHandle.IsSubclassOf(typeof(CharacterGroundAnimationsReference)))
		{
			if (!(AllCharacterGroundAnimations.Find(KeyHash.Make(id)) is T result22) || 1 == 0)
			{
				throw new NullReferenceException(typeFromHandle.Name + " is null for '" + id + "'");
			}
			return result22;
		}
		if (typeFromHandle == typeof(CharacterTexturesReference) || typeFromHandle.IsSubclassOf(typeof(CharacterTexturesReference)))
		{
			if (!(AllCharacterTextures.Find(KeyHash.Make(id)) is T result23) || 1 == 0)
			{
				throw new NullReferenceException(typeFromHandle.Name + " is null for '" + id + "'");
			}
			return result23;
		}
		if (typeFromHandle == typeof(SituationTemplate) || typeFromHandle.IsSubclassOf(typeof(SituationTemplate)))
		{
			if (!(AllSituations.Find(KeyHash.Make(id)) is T result24) || 1 == 0)
			{
				throw new NullReferenceException(typeFromHandle.Name + " is null for '" + id + "'");
			}
			return result24;
		}
		if (typeFromHandle == typeof(ParticleEmitterReference) || typeFromHandle.IsSubclassOf(typeof(ParticleEmitterReference)))
		{
			if (!(AllParticleEmitters.Find(KeyHash.Make(id)) is T result25) || 1 == 0)
			{
				throw new NullReferenceException(typeFromHandle.Name + " is null for '" + id + "'");
			}
			return result25;
		}
		if (typeFromHandle == typeof(ExplosionReference))
		{
			if (!(AllExplosions.Find(KeyHash.Make(id)) is T result26) || 1 == 0)
			{
				throw new NullReferenceException(typeFromHandle.Name + " is null for '" + id + "'");
			}
			return result26;
		}
		if (typeFromHandle == typeof(DistantGlintReference) || typeFromHandle.IsSubclassOf(typeof(DistantGlintReference)))
		{
			if (!(AllDistantGlints.Find(KeyHash.Make(id)) is T result27) || 1 == 0)
			{
				throw new NullReferenceException(typeFromHandle.Name + " is null for '" + id + "'");
			}
			return result27;
		}
		if (typeFromHandle == typeof(ChannelGroupReference) || typeFromHandle.IsSubclassOf(typeof(ChannelGroupReference)))
		{
			if (!(AllChannelGroups.Find(KeyHash.Make(id)) is T result28) || 1 == 0)
			{
				throw new NullReferenceException(typeFromHandle.Name + " is null for '" + id + "'");
			}
			return result28;
		}
		if (typeFromHandle == typeof(DspReference) || typeFromHandle.IsSubclassOf(typeof(DspReference)))
		{
			if (!(AllDsp.Find(KeyHash.Make(id)) is T result29) || 1 == 0)
			{
				throw new NullReferenceException(typeFromHandle.Name + " is null for '" + id + "'");
			}
			return result29;
		}
		if (typeFromHandle == typeof(ClutterObjectTemplate) || typeFromHandle.IsSubclassOf(typeof(ClutterObjectTemplate)))
		{
			if (!(AllClutterObjects.Find(KeyHash.Make(id)) is T result30) || 1 == 0)
			{
				throw new NullReferenceException(typeFromHandle.Name + " is null for '" + id + "'");
			}
			return result30;
		}
		if (typeFromHandle == typeof(StaticObjectTemplate) || typeFromHandle.IsSubclassOf(typeof(StaticObjectTemplate)))
		{
			if (!(AllStaticObjects.Find(KeyHash.Make(id)) is T result31) || 1 == 0)
			{
				throw new NullReferenceException(typeFromHandle.Name + " is null for '" + id + "'");
			}
			return result31;
		}
		if (typeFromHandle == typeof(StaticSubObjectTemplate) || typeFromHandle.IsSubclassOf(typeof(StaticSubObjectTemplate)))
		{
			if (!(AllStaticSubObjects.Find(KeyHash.Make(id)) is T result32) || 1 == 0)
			{
				throw new NullReferenceException(typeFromHandle.Name + " is null for '" + id + "'");
			}
			return result32;
		}
		throw new NullReferenceException($"type '{typeFromHandle.Name}' not implemented for modlibrary (item '{id}')");
	}

	public static void PopulateMeshCollections()
	{
		foreach (AstronomicalTemplate item in TemplateLookup.GetList())
		{
			if (item.MeshCollection != null)
			{
				item.MeshCollection = item.MeshCollection.Populate();
			}
			foreach (AstronomicalTemplate body in item.Bodies)
			{
				if (body.MeshCollection != null)
				{
					body.MeshCollection = body.MeshCollection.Populate();
				}
			}
		}
	}

	public static void PopulateHeightmapCollections()
	{
		foreach (AstronomicalTemplate item in TemplateLookup.GetList())
		{
			if (item.HeightmapCollection != null)
			{
				item.HeightmapCollection = item.HeightmapCollection.Populate();
			}
			item.UpdateTextureReferences();
			foreach (AstronomicalTemplate body in item.Bodies)
			{
				if (body.HeightmapCollection != null)
				{
					body.HeightmapCollection = body.HeightmapCollection.Populate();
				}
				body.UpdateTextureReferences();
			}
		}
	}

	public static void PopulateSounds()
	{
		foreach (SoundBehavior item in AllSoundBehaviours.GetList())
		{
			item.Populate();
		}
		foreach (MusicPlayList item2 in AllMusicPlaylists.GetList())
		{
			item2.Populate();
		}
		foreach (ChannelGroupReference item3 in AllChannelGroups.GetList())
		{
			item3.Populate();
		}
	}

	public static void PrepareCanvases(RendererContext rendererContext)
	{
	}

	public static void Register(AstronomicalTemplate astronomical)
	{
		TemplateLookup.Register(astronomical);
	}

	[TerminalAction("mods", "list all mods", ArgParseMode.Default)]
	public static void AllModsToConsole()
	{
		Lookup.ToConsole();
	}

	[TerminalAction("files", "list all files", ArgParseMode.Default)]
	public static void AllFilesToConsole()
	{
		AllFiles.ToConsole();
	}

	[TerminalAction("meshes", "list all meshes", ArgParseMode.Default)]
	public static void AllMeshesToConsole()
	{
		AllMeshes.ToConsole<MeshReference>();
	}

	[TerminalAction("languagefiles", "list all language files", ArgParseMode.Default)]
	public static void AllLanguageFilesToConsole()
	{
		AllLanguageFiles.ToConsole();
	}

	[TerminalAction("templates", "list all templates", ArgParseMode.Default)]
	public static void AllTemplatesToConsole()
	{
		TemplateLookup.ToConsole();
	}

	[TerminalAction("unusedfiles", "list all unused files of the provided types", ArgParseMode.Unparsed)]
	public static void UnusedFilesToConsole(string input)
	{
		string contentPath = Path.Combine("Content");
		bool shouldAppend;
		if (!Directory.Exists(contentPath))
		{
			LogCategory log = DefaultCategory.Log;
			LogCategory logCategory = log;
			LogString<Error> message = new LogString<Error>(30, 1, log, out shouldAppend);
			if (shouldAppend)
			{
				message.AppendLiteral("Content directory '");
				message.AppendFormatted(contentPath);
				message.AppendLiteral("' not found");
			}
			logCategory.Error(message, "UnusedFilesToConsole", "C:\\prototype-planet\\KSA\\ModLibrary.cs", 2407);
			return;
		}
		if (string.IsNullOrEmpty(input))
		{
			input = "*.*";
		}
		string[] array = input.Split(' ');
		List<FileRef> list = new List<FileRef>();
		int num = 0;
		StringBuilder stringBuilder = new StringBuilder();
		string[] array2 = array;
		foreach (string text in array2)
		{
			string extension = Path.GetExtension(text);
			extension = new string(extension.Where(char.IsLetterOrDigit).ToArray());
			if (!string.IsNullOrEmpty(extension))
			{
				stringBuilder.Append('_');
				stringBuilder.Append(extension);
			}
			List<string> list2 = (from file in Directory.GetFiles(contentPath, text, SearchOption.AllDirectories)
				select Path.GetRelativePath(contentPath, file)).ToList();
			num += list2.Count;
			for (int num2 = list2.Count - 1; num2 >= 0; num2--)
			{
				string text2 = list2[num2];
				if (!Contains(Path.Combine("Content", text2)))
				{
					list.Add(new FileRef(text2));
				}
			}
		}
		int totalWidth = list.Select((FileRef file) => file.Path.Length).DefaultIfEmpty(0).Max();
		long bytes = list.Select((FileRef file) => file.File.Length).DefaultIfEmpty(0L).Sum();
		ByteReference value = new ByteReference(Mod.Empty, bytes);
		list.Sort((FileRef a, FileRef b) => a.File.Length.CompareTo(b.File.Length));
		list.Reverse();
		int length = list.Count.ToString().Length;
		string text3 = Path.Combine(Constants.DocumentsFolderPath, $"UnusedFiles{stringBuilder}.txt");
		if (File.Exists(text3))
		{
			File.Delete(text3);
		}
		using StreamWriter streamWriter = new StreamWriter(text3);
		LogCategory log2 = DefaultCategory.Log;
		LogCategory logCategory2 = log2;
		LogString<Information> message2 = new LogString<Information>(70, 4, log2, out shouldAppend);
		if (shouldAppend)
		{
			message2.AppendLiteral("Found ");
			message2.AppendFormatted(list.Count);
			message2.AppendLiteral(" unused files matching '");
			message2.AppendFormatted(input);
			message2.AppendLiteral("' out of a total of ");
			message2.AppendFormatted(num);
			message2.AppendLiteral(" files with ");
			message2.AppendFormatted(value);
			message2.AppendLiteral(" in size");
		}
		logCategory2.Info(message2, "UnusedFilesToConsole", "C:\\prototype-planet\\KSA\\ModLibrary.cs", 2469);
		for (int num3 = 0; num3 < list.Count; num3++)
		{
			FileRef fileRef = list[num3];
			string value2 = num3.ToString().PadRight(length, ' ');
			string value3 = fileRef.Path.PadRight(totalWidth, ' ');
			LogCategory log3 = DefaultCategory.Log;
			LogCategory logCategory3 = log3;
			LogString<Debug> message3 = new LogString<Debug>(2, 3, log3, out shouldAppend);
			if (shouldAppend)
			{
				message3.AppendFormatted(value2);
				message3.AppendLiteral("\t");
				message3.AppendFormatted(value3);
				message3.AppendLiteral("\t");
				message3.AppendFormatted(fileRef.Size);
			}
			logCategory3.Debug(message3, "UnusedFilesToConsole", "C:\\prototype-planet\\KSA\\ModLibrary.cs", 2475);
			streamWriter.WriteLine($"{value2}\t{value3}\t{fileRef.Size}");
		}
		LogCategory log4 = DefaultCategory.Log;
		LogCategory logCategory4 = log4;
		message2 = new LogString<Information>(32, 1, log4, out shouldAppend);
		if (shouldAppend)
		{
			message2.AppendLiteral("Exported unused files list to '");
			message2.AppendFormatted(text3);
			message2.AppendLiteral("'");
		}
		logCategory4.Info(message2, "UnusedFilesToConsole", "C:\\prototype-planet\\KSA\\ModLibrary.cs", 2478);
	}

	private static bool Contains(string file)
	{
		file = file.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
		List<FileReference> list = AllFiles.GetList();
		foreach (FileReference item in list)
		{
			string modPath = item.ModPath;
			modPath = modPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
			if (string.Equals(modPath, file, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	[TerminalAction("textures", "without arguments list all textures or can be performs texture operations", ArgParseMode.Unparsed)]
	public static void AllTexturesToConsole(string input = "")
	{
		string[] array = input.Split(' ');
		if (string.IsNullOrEmpty(input) || array.Length == 0)
		{
			AllFiles.ToConsole<TextureReference>();
			return;
		}
		if (!_textureActions.TryGet(array[0], out var o))
		{
			LogCategory log = DefaultCategory.Log;
			LogCategory logCategory = log;
			LogString<Error> message = new LogString<Error>(46, 2, log, out var shouldAppend);
			if (shouldAppend)
			{
				message.AppendLiteral("Unknown texture action '");
				message.AppendFormatted(array[0]);
				message.AppendLiteral("', available actions: ");
				message.AppendFormatted(_textureActions.PrintAll());
			}
			logCategory.Error(message, "AllTexturesToConsole", "C:\\prototype-planet\\KSA\\ModLibrary.cs", 2519);
			return;
		}
		switch (o)
		{
		case TextureAction.List:
			AllFiles.ToConsole<TextureReference>();
			return;
		case TextureAction.Summarize:
			GenerateTextureSummary();
			return;
		}
		throw new NotImplementedException($"action {o} is not implemented");
	}

	private static void AddToTextureSummary(XElement root, XElement vesselElement, Dictionary<BucketType, SizeResult> vesselBuckets)
	{
		Span<char> buffer = stackalloc char[128];
		vesselElement.Add(new XAttribute("Size", ByteReference.ToNearest((ByteSize)vesselBuckets.Sum((KeyValuePair<BucketType, SizeResult> b) => (int)b.Value.Size), buffer).ToString()));
		foreach (KeyValuePair<BucketType, SizeResult> vesselBucket in vesselBuckets)
		{
			XElement content = new XElement("Texture", new XAttribute("Width", vesselBucket.Key.Width), new XAttribute("Height", vesselBucket.Key.Height), new XAttribute("Count", vesselBucket.Value.Count), new XAttribute("Size", ByteReference.ToNearest(vesselBucket.Value.Size, buffer).ToString()));
			vesselElement.Add(content);
		}
		root.Add(vesselElement);
	}

	private static void GenerateTextureSummary()
	{
		if (Universe.CurrentSystem == null)
		{
			DefaultCategory.Log.Error("No current system selected, cannot generate texture summary.", "GenerateTextureSummary", "C:\\prototype-planet\\KSA\\ModLibrary.cs", 2558);
			return;
		}
		XDocument xDocument = new XDocument();
		XElement xElement = new XElement("TextureSummary");
		xDocument.Add(xElement);
		Dictionary<BucketType, SizeResult> dictionary = new Dictionary<BucketType, SizeResult>();
		Dictionary<BucketType, SizeResult> dictionary2 = new Dictionary<BucketType, SizeResult>();
		Dictionary<BucketType, SizeResult> dictionary3 = new Dictionary<BucketType, SizeResult>();
		List<FileReference> list = AllFiles.GetList();
		int num = 0;
		long num2 = 0L;
		foreach (FileReference item in list)
		{
			if (item is TextureReference { IsVirtual: false } textureReference)
			{
				switch (textureReference.Category)
				{
				case TextureCategory.Vessel:
				{
					dictionary.TryAdd(textureReference.BucketType, new SizeResult());
					SizeResult sizeResult3 = dictionary[textureReference.BucketType];
					sizeResult3.Count = (int)sizeResult3.Count + 1;
					dictionary[textureReference.BucketType].Size += ByteSize.Of(textureReference.Size);
					num++;
					num2 += textureReference.Size;
					break;
				}
				case TextureCategory.Terrain:
				{
					dictionary2.TryAdd(textureReference.BucketType, new SizeResult());
					SizeResult sizeResult2 = dictionary2[textureReference.BucketType];
					sizeResult2.Count = (int)sizeResult2.Count + 1;
					dictionary2[textureReference.BucketType].Size += ByteSize.Of(textureReference.Size);
					num++;
					num2 += textureReference.Size;
					break;
				}
				default:
				{
					dictionary3.TryAdd(textureReference.BucketType, new SizeResult());
					SizeResult sizeResult = dictionary3[textureReference.BucketType];
					sizeResult.Count = (int)sizeResult.Count + 1;
					dictionary3[textureReference.BucketType].Size += ByteSize.Of(textureReference.Size);
					num++;
					num2 += textureReference.Size;
					break;
				}
				}
			}
		}
		AddToTextureSummary(xElement, new XElement("VesselTextures"), Sort(dictionary));
		AddToTextureSummary(xElement, new XElement("TerrainTextures"), Sort(dictionary2));
		AddToTextureSummary(xElement, new XElement("UnscaledTextures"), Sort(dictionary3));
		string text = Path.Combine(Constants.DocumentsFolderPath, Universe.CurrentSystem.Id + "_Summary.xml");
		if (File.Exists(text))
		{
			File.Delete(text);
		}
		string value = ByteReference.AllocateNearestString(num2);
		xElement.Add(new XAttribute("Size", value));
		using (StreamWriter textWriter = new StreamWriter(text))
		{
			xDocument.Save(textWriter);
			LogCategory log = DefaultCategory.Log;
			LogCategory logCategory = log;
			LogString<Information> message = new LogString<Information>(81, 4, log, out var shouldAppend);
			if (shouldAppend)
			{
				message.AppendLiteral("generated texture summary for system '");
				message.AppendFormatted(Universe.CurrentSystem.Id);
				message.AppendLiteral("' at '");
				message.AppendFormatted(text);
				message.AppendLiteral("' for ");
				message.AppendFormatted(num);
				message.AppendLiteral(" textures with a total size of ");
				message.AppendFormatted(value);
			}
			logCategory.Info(message, "GenerateTextureSummary", "C:\\prototype-planet\\KSA\\ModLibrary.cs", 2630);
		}
		static int Comparison(BucketType a, BucketType b)
		{
			int value2 = a.Width * a.Height;
			return (b.Width * b.Height).CompareTo(value2);
		}
		static Dictionary<BucketType, SizeResult> Sort(Dictionary<BucketType, SizeResult> input)
		{
			return input.OrderBy((KeyValuePair<BucketType, SizeResult> kvp) => kvp.Key, Comparer<BucketType>.Create(Comparison)).ToDictionary((KeyValuePair<BucketType, SizeResult> kvp) => kvp.Key, (KeyValuePair<BucketType, SizeResult> kvp) => kvp.Value);
		}
	}

	[TerminalAction("materials", "list all materials", ArgParseMode.Default)]
	public static void AllMaterialsToConsole()
	{
		AllMaterials.ToConsole<PbrMaterialReference>();
	}

	[TerminalAction("gauges", "list all gauges", ArgParseMode.Default)]
	public static void AllGaugesToConsole()
	{
		AllGaugeComponents.ToConsole<GaugeComponent>();
	}

	[TerminalAction("canvases", "list all canvases", ArgParseMode.Default)]
	public static void AllCanvasesToConsole()
	{
		AllGaugeCanvases.ToConsole<GaugeCanvas>();
	}

	[TerminalAction("shaders", "list all shaders", ArgParseMode.Default)]
	public static void AllShadersToConsole()
	{
		AllFiles.ToConsole<ShaderReference>();
	}

	public static void RegisterCommands(Program program)
	{
		Program.TerminalInterface.RegisterCommand(new Action(AllModsToConsole), program);
		Program.TerminalInterface.RegisterCommand(new Action(AllFilesToConsole), program);
		Program.TerminalInterface.RegisterCommand(new <>f__AnonymousDelegate1<string>(AllTexturesToConsole), program);
		Program.TerminalInterface.RegisterCommand(new Action(AllMaterialsToConsole), program);
		Program.TerminalInterface.RegisterCommand(new Action(AllShadersToConsole), program);
		Program.TerminalInterface.RegisterCommand(new Action(AllGaugesToConsole), program);
		Program.TerminalInterface.RegisterCommand(new Action(AllCanvasesToConsole), program);
		Program.TerminalInterface.RegisterCommand(new Action(AllMeshesToConsole), program);
		Program.TerminalInterface.RegisterCommand(new Action(AllLanguageFilesToConsole), program);
		Program.TerminalInterface.RegisterCommand(new Action(AllTemplatesToConsole), program);
		Program.TerminalInterface.RegisterCommand(new Action<string>(UnusedFilesToConsole), program);
		Program.TerminalInterface.RegisterCommand(new Action<string>(GenerateStarBinary), program);
	}

	[TerminalAction("generatestarbinary", "generates a star binary fil dat file", ArgParseMode.Default)]
	public static void GenerateStarBinary(string localPath)
	{
		string text = Path.Combine(Constants.DocumentsFolderPath, localPath);
		bool shouldAppend;
		if (!File.Exists(text))
		{
			LogCategory log = DefaultCategory.Log;
			LogCategory logCategory = log;
			LogString<Error> message = new LogString<Error>(33, 1, log, out shouldAppend);
			if (shouldAppend)
			{
				message.AppendLiteral("error source dat file not found: ");
				message.AppendFormatted(text);
			}
			logCategory.Error(message, "GenerateStarBinary", "C:\\prototype-planet\\KSA\\ModLibrary.cs", 2668);
			return;
		}
		LogCategory log2 = DefaultCategory.Log;
		LogCategory logCategory2 = log2;
		LogString<Information> message2 = new LogString<Information>(19, 1, log2, out shouldAppend);
		if (shouldAppend)
		{
			message2.AppendLiteral("loading stars from ");
			message2.AppendFormatted(text);
		}
		logCategory2.Info(message2, "GenerateStarBinary", "C:\\prototype-planet\\KSA\\ModLibrary.cs", 2671);
		string text2 = Path.ChangeExtension(text, ".toml");
		if (!File.Exists(text2))
		{
			LogCategory log3 = DefaultCategory.Log;
			LogCategory logCategory3 = log3;
			LogString<Error> message = new LogString<Error>(27, 1, log3, out shouldAppend);
			if (shouldAppend)
			{
				message.AppendLiteral("error TOML file not found: ");
				message.AppendFormatted(text2);
			}
			logCategory3.Error(message, "GenerateStarBinary", "C:\\prototype-planet\\KSA\\ModLibrary.cs", 2677);
			return;
		}
		StarImportSettings starImportSettings = TomletMain.To<StarImportSettings>(File.ReadAllText(text2));
		float starScale = starImportSettings.StarScale;
		float minBrightness = starImportSettings.MinBrightness;
		float magnitudeScale = starImportSettings.MagnitudeScale;
		float magnitudeOffset = starImportSettings.MagnitudeOffset;
		float brightnessDimStart = starImportSettings.BrightnessDimStart;
		float brightnessDimEnd = starImportSettings.BrightnessDimEnd;
		IEnumerable<string> enumerable = File.ReadLines(text);
		string text3 = Path.ChangeExtension(text, ".bin");
		using BinaryWriter binaryWriter = new BinaryWriter(File.OpenWrite(text3));
		int num = 0;
		int num2 = 0;
		binaryWriter.Write(num);
		foreach (string item in enumerable)
		{
			try
			{
				string[] columns = item.Split(starImportSettings.Delimiter);
				double num3 = double.Parse(starImportSettings.RightAscension.Get(columns)) * (Math.PI / 180.0);
				double num4 = double.Parse(starImportSettings.Declination.Get(columns)) * (Math.PI / 180.0);
				float num5 = float.Parse(starImportSettings.Magnitude.Get(columns), CultureInfo.InvariantCulture);
				float value = starScale * MathF.Pow(10f, magnitudeScale * (magnitudeOffset - num5));
				value = Math.Clamp(value, 0f, 30f);
				float num6 = 1f;
				if (value < brightnessDimStart)
				{
					float num7 = Math.Clamp((brightnessDimStart - value) / (brightnessDimStart - brightnessDimEnd), 0f, 1f);
					num6 = 1f - num7;
					num6 *= num6;
				}
				value = Math.Max(value, brightnessDimStart);
				byte value2 = (byte)(value / 30f * 255f);
				double bv = double.Parse(starImportSettings.BvColor.Get(columns), CultureInfo.InvariantCulture);
				double3 vector = new double3(Math.Cos(num4) * Math.Cos(num3), Math.Sin(num4), Math.Cos(num4) * Math.Sin(num3));
				byte4 byte5 = Astronomical.BvToRgb(bv);
				byte4 byte6 = new byte4((byte)((float)(int)byte5.X / 255f * num6 * 255f), (byte)((float)(int)byte5.Y / 255f * num6 * 255f), (byte)((float)(int)byte5.Z / 255f * num6 * 255f), byte.MaxValue);
				if ((float)(int)byte6.X / 255f < minBrightness && (float)(int)byte6.Y / 255f < minBrightness && (float)(int)byte6.Z / 255f < minBrightness)
				{
					num2++;
					continue;
				}
				float3 float5 = float3.Pack(in vector, Pack.Float.Normalize);
				binaryWriter.Write(float5.X);
				binaryWriter.Write(float5.Y);
				binaryWriter.Write(float5.Z);
				binaryWriter.Write(value2);
				binaryWriter.Write(byte6.X);
				binaryWriter.Write(byte6.Y);
				binaryWriter.Write(byte6.Z);
				num++;
			}
			catch (Exception)
			{
			}
		}
		binaryWriter.BaseStream.Seek(0L, SeekOrigin.Begin);
		binaryWriter.Write(num);
		binaryWriter.Close();
		LogCategory log4 = DefaultCategory.Log;
		LogCategory logCategory4 = log4;
		message2 = new LogString<Information>(20, 2, log4, out shouldAppend);
		if (shouldAppend)
		{
			message2.AppendLiteral("generated ");
			message2.AppendFormatted(num);
			message2.AppendLiteral(" stars in ");
			message2.AppendFormatted(text3);
		}
		logCategory4.Info(message2, "GenerateStarBinary", "C:\\prototype-planet\\KSA\\ModLibrary.cs", 2769);
		if (num2 > 0)
		{
			LogCategory log5 = DefaultCategory.Log;
			LogCategory logCategory5 = log5;
			LogString<Warning> message3 = new LogString<Warning>(20, 1, log5, out shouldAppend);
			if (shouldAppend)
			{
				message3.AppendLiteral("skipped ");
				message3.AppendFormatted(num2);
				message3.AppendLiteral(" black stars");
			}
			logCategory5.Warning(message3, "GenerateStarBinary", "C:\\prototype-planet\\KSA\\ModLibrary.cs", 2771);
		}
	}

	public static void Bind(Renderer renderer)
	{
		Task task = Parallel.ForEachAsync(Binders, (IBinder binder, CancellationToken token) =>
		{
			using StagingPool stagingPool = renderer.Allocator.CreateStagingPool(renderer.Graphics, 1);
			binder.Bind(renderer, stagingPool);
			return ValueTask.CompletedTask;
		});
		while (!task.Wait(1))
		{
			Loading.Current?.OnFrame();
		}
	}

	public static void AttachGameData()
	{
		foreach (PartGameDataReference item in AllPartGameDataReferences.GetList())
		{
			for (int i = 0; i < AllParts.GetList().Count; i++)
			{
				PartTemplate partTemplate = AllParts.GetList()[i];
				if (item.Id.Equals(partTemplate.Id))
				{
					partTemplate.ApplyGameData(item);
					break;
				}
			}
		}
		foreach (ClutterObjectGameDataReference item2 in AllClutterObjectGameData.GetList())
		{
			ClutterObjectTemplate clutterObjectTemplate = AllClutterObjects.Find(KeyHash.Make(item2.Id));
			if (clutterObjectTemplate == null)
			{
				DefaultCategory.Log.Error("Clutter object game data '" + item2.Id + "' does not match any clutter object asset", "AttachGameData", "C:\\prototype-planet\\KSA\\ModLibrary.cs", 2813);
			}
			else
			{
				clutterObjectTemplate.ApplyGameData(item2);
			}
		}
		foreach (StaticObjectGameDataReference item3 in AllStaticObjectGameData.GetList())
		{
			StaticObjectTemplate staticObjectTemplate = AllStaticObjects.Find(KeyHash.Make(item3.Id));
			if (staticObjectTemplate == null)
			{
				DefaultCategory.Log.Error("Static object game data '" + item3.Id + "' does not match any static object asset", "AttachGameData", "C:\\prototype-planet\\KSA\\ModLibrary.cs", 2825);
			}
			else
			{
				staticObjectTemplate.ApplyGameData(item3);
			}
		}
		foreach (PartTemplate item4 in AllParts.GetList())
		{
			if (!item4.IsSubPart)
			{
				item4.ResolveConsumerFeedPoints();
			}
		}
		StaticObject.ResolveAll();
	}

	public static bool HasNewMods()
	{
		if (!PrepareManifest())
		{
			return false;
		}
		foreach (ModEntry mod in Manifest.Mods)
		{
			if (mod.New)
			{
				return true;
			}
		}
		return false;
	}

	public static void PreloadLanguages()
	{
		foreach (Mod item in Lookup.GetList())
		{
			if (item.AssetBundles.Count == 0)
			{
				break;
			}
			foreach (AssetBundle assetBundle in item.AssetBundles)
			{
				List<SerializedId> assets = assetBundle.Assets;
				foreach (SerializedId item2 in assets)
				{
					if (item2 is LanguageFileReference languageFileReference)
					{
						languageFileReference.OnDataLoad(item);
						languageFileReference.Load();
					}
				}
			}
		}
	}
}
