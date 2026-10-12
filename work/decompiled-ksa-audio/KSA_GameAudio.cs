using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Brutal.FmodApi;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Brutal.Strings;

namespace KSA;

public static class GameAudio
{
	public static readonly Random AudioRandom = new Random();

	public static List<IChannel?> ActiveChannels = new List<IChannel>();

	public static readonly List<IAudio> AllIAudio = new List<IAudio>(1024);

	private unsafe static FileOpenCallback _openCallback = OpenStream;

	private static FileCloseCallback _closeCallback = CloseStream;

	private unsafe static FileReadCallback _readCallback = ReadStream;

	private static FileSeekCallback _seekCallback = SeekStream;

	public const uint STREAM_BUFFER_SIZE = 32768u;

	public const int MAX_CHANNELS = 1024;

	public const float DOPPLER_SCALE = 1f;

	public const float DISTANCE_FACTOR = 1f;

	public const float ROLL_OFF_FACTOR = 0.1f;

	public static List<ClusterChannelWrapper> ClusterChannels = new List<ClusterChannelWrapper>(16);

	public const double MAX_SIM_SPEED_FOR_SOUND = 10.0;

	private static IChannel? _testMusicChannel;

	public static bool ShowSoundPositions;

	private const float SOUND_MARKER_RADIUS = 4f;

	private const double MIN_SPATIAL_DISTANCE = 0.001;

	private static string[] _biomeNames = new string[4];

	private static float[] _biomeValues = new float[4];

	public static FmodSystem System { get; private set; }

	private static ChannelGroup MasterGroup { get; set; }

	private static ChannelGroup SfxGroup { get; set; }

	private static ChannelGroup UiGroup { get; set; }

	private static ChannelGroup MusicGroup { get; set; }

	public static Camera GetAudioCamera()
	{
		return Program.MainViewport.BaseCamera;
	}

	public static ulong MsToSamples(double ms)
	{
		System.TryGetSoftwareFormat(out var sampleRate, out var _, out var numRawSpeakers);
		return (ulong)(ms / 1000.0 * (double)sampleRate * (double)numRawSpeakers);
	}

	public static void Register(IChannel channel)
	{
		ActiveChannels.Add(channel);
	}

	public static void Register(IAudio iAudio)
	{
		AllIAudio.Add(iAudio);
	}

	public static void Deregister(IAudio iAudio)
	{
		iAudio.StopAudio(immediate: true);
		foreach (ClusterChannelWrapper clusterChannel in iAudio.ClusterChannels)
		{
			clusterChannel.Stop(stopImmediate: true);
			clusterChannel.ClearSubscribers();
		}
		iAudio.ClusterChannels.Clear();
		AllIAudio.Remove(iAudio);
	}

	public static void SetChannelGroupVolume(ChannelGroup group, float volume)
	{
		volume = Math.Clamp(volume, 0f, 1f);
		group.TrySetVolume(volume);
	}

	public static void OnApplicationStart()
	{
		System = Fmod.SystemCreate(131848u);
		System.Init(1024, InitFlags._3dRightHanded, 0);
		System.SetStreamBufferSize(32768u, Brutal.FmodApi.TimeUnit.RawBytes);
		System.Set3dSettings(1f, 1f, 0.1f);
		System.SetFileSystem(_openCallback, _closeCallback, _readCallback, _seekCallback, default, default, 2048);
		for (int i = 0; i < EnumCollections.ChannelGroupTypes.Values.Length; i++)
		{
			ChannelGroupType channelGroupType = EnumCollections.ChannelGroupTypes.Values[i];
			if (channelGroupType == ChannelGroupType.None)
			{
				continue;
			}
			string id = EnumCollections.ChannelGroupTypes.Names[i];
			if (CreateFmodChannelGroup(id, out var channelGroup, out var _))
			{
				ModLibrary.Register(new ChannelGroupReference(channelGroupType, channelGroup));
				switch (channelGroupType)
				{
				case ChannelGroupType.Master:
					MasterGroup = channelGroup;
					break;
				case ChannelGroupType.Sfx:
					SfxGroup = channelGroup;
					break;
				case ChannelGroupType.Ui:
					UiGroup = channelGroup;
					break;
				case ChannelGroupType.Music:
					MusicGroup = channelGroup;
					break;
				default:
					throw new ArgumentOutOfRangeException();
				}
			}
		}
		ApplySettings();
	}

	public static void Release()
	{
		System.Close();
		System.Release();
	}

	public static void UpdateAudio(double deltaTime)
	{
		System.Update();
		LocationMusicPlayer.Update(deltaTime);
		ExplosionSoundSystem.UpdateAudio();
		foreach (IAudio item in AllIAudio)
		{
			item.UpdateAudio();
			for (int num = item.ClusterChannels.Count - 1; num >= 0; num--)
			{
				ClusterChannelWrapper clusterChannelWrapper = item.ClusterChannels[num];
				bool flag = clusterChannelWrapper.IsPlaying();
				if (flag)
				{
					clusterChannelWrapper.UpdateSpatialAudio();
				}
				if (clusterChannelWrapper.IsStopping || !flag)
				{
					clusterChannelWrapper.ClearSubscribers();
					item.ClusterChannels.RemoveAt(num);
				}
			}
		}
		for (int num2 = ClusterChannels.Count - 1; num2 >= 0; num2--)
		{
			ClusterChannelWrapper clusterChannelWrapper2 = ClusterChannels[num2];
			bool flag2 = clusterChannelWrapper2.IsPlaying();
			if (flag2)
			{
				clusterChannelWrapper2.UpdateSpatialAudio();
			}
			if (clusterChannelWrapper2.IsStopping || !flag2)
			{
				clusterChannelWrapper2.ClearSubscribers();
				ClusterChannels.RemoveAt(num2);
			}
		}
		for (int num3 = ActiveChannels.Count - 1; num3 >= 0; num3--)
		{
			IChannel channel = ActiveChannels[num3];
			if (channel == null)
			{
				ActiveChannels.RemoveAt(num3);
			}
			else
			{
				channel.HandleFade(deltaTime);
				channel.HandleSimSpeedPitch();
				channel.StopForSimSpeed();
				channel.ApplyParameters();
				channel.UpdateVolume();
				channel.UpdatePitch();
				channel.UpdateLpf();
				channel.UpdateHpf();
				if (!IsStillActive(channel))
				{
					channel.Stop();
					ActiveChannels.RemoveAt(num3);
				}
			}
		}
		foreach (ChannelGroupReference item2 in ModLibrary.AllChannelGroups.GetList())
		{
			item2.ApplyParameters();
		}
	}

	public static bool IsStillActive(IChannel? channel)
	{
		if (channel == null)
		{
			return false;
		}
		bool flag = channel.IsPlaying();
		bool flag2 = channel.IsVirtual();
		return flag | flag2;
	}

	private unsafe static void LoadFileMemory(string name, out byte* buffer, out int length)
	{
		using FileStream fileStream = File.OpenRead(name);
		length = (int)fileStream.Length;
		buffer = (byte*)NativeMemory.Alloc((nuint)fileStream.Length);
		int num = fileStream.Read(new Span<byte>(buffer, length));
		if (num != length)
		{
			throw new Exception("Failed to read the entire file");
		}
	}

	public static bool CreateFmodSoundGroup(SoundGroupData data, out SoundGroup soundGroup, out Result result)
	{
		int byteCount = Utf8.GetByteCount(data.Id);
		Span<byte> span = stackalloc byte[byteCount + 1];
		span[byteCount] = 0;
		Utf8.GetBytes(data.Id, span);
		result = System.TryCreateSoundGroup(span, out soundGroup);
		return result == Result.Ok;
	}

	public static bool CreateFmodChannelGroup(ChannelGroupReference data, out ChannelGroup channelGroup, out Result result)
	{
		int byteCount = Utf8.GetByteCount(data.Id);
		Span<byte> span = stackalloc byte[byteCount + 1];
		span[byteCount] = 0;
		Utf8.GetBytes(data.Id, span);
		result = System.TryCreateChannelGroup(span, out channelGroup);
		return result == Result.Ok;
	}

	public static bool CreateFmodChannelGroup(string id, out ChannelGroup channelGroup, out Result result)
	{
		int byteCount = Utf8.GetByteCount(id);
		Span<byte> span = stackalloc byte[byteCount + 1];
		span[byteCount] = 0;
		Utf8.GetBytes(id, span);
		result = System.TryCreateChannelGroup(span, out channelGroup);
		return result == Result.Ok;
	}

	public unsafe static bool CreateFmodSound(SoundFileReference soundReference, out Sound sound, out Result result)
	{
		if (soundReference.StreamFromDisk)
		{
			int byteCount = Utf8.GetByteCount(soundReference.ModPath);
			Span<byte> span = stackalloc byte[byteCount + 1];
			span[byteCount] = 0;
			Utf8.GetBytes(soundReference.ModPath, span);
			result = System.TryCreateStream(span, soundReference.Mode, new CreateSoundExInfo(), out sound);
			if (result == Result.Ok)
			{
				bool flag = false;
				while (!flag)
				{
					sound.TryGetOpenState(out var openState, out var _, out var _, out var _);
					if (openState == OpenState.Ready)
					{
						flag = true;
					}
					Thread.Sleep(1);
				}
			}
		}
		else
		{
			LoadFileMemory(soundReference.ModPath, out var buffer, out var length);
			CreateSoundExInfo createSoundExInfo = new CreateSoundExInfo();
			createSoundExInfo.Length = (uint)length;
			CreateSoundExInfo exInfo = createSoundExInfo;
			result = System.TryCreateSound(new ReadOnlySpan<byte>(buffer, length), soundReference.Mode | Mode.OpenMemory, in exInfo, out sound);
			NativeMemory.Free(buffer);
		}
		if (result == Result.Ok)
		{
			soundReference.LoopData?.Apply(sound);
		}
		return result == Result.Ok;
	}

	private unsafe static Result OpenStream(byte* name, uint* filesize, nint* handle, nint userdata)
	{
		string text = Marshal.PtrToStringUTF8((nint)name);
		if (text == null)
		{
			return Result.ErrFileNotFound;
		}
		FileStream fileStream = File.OpenRead(text);
		*filesize = (uint)fileStream.Length;
		*handle = GCHandle.ToIntPtr(GCHandle.Alloc(fileStream));
		return Result.Ok;
	}

	private static Result CloseStream(nint handle, nint userdata)
	{
		GCHandle gCHandle = GCHandle.FromIntPtr(handle);
		if (!(gCHandle.Target is FileStream fileStream))
		{
			return Result.ErrInvalidHandle;
		}
		fileStream.Close();
		gCHandle.Free();
		return Result.Ok;
	}

	private unsafe static Result ReadStream(nint handle, nint buffer, uint sizeBytes, uint* bytesRead, nint userData)
	{
		if (!(GCHandle.FromIntPtr(handle).Target is FileStream fileStream))
		{
			return Result.ErrInvalidHandle;
		}
		Span<byte> buffer2 = new Span<byte>((void*)buffer, (int)sizeBytes);
		*bytesRead = (uint)fileStream.Read(buffer2);
		if (*bytesRead < sizeBytes)
		{
			return Result.ErrFileEof;
		}
		return Result.Ok;
	}

	private static Result SeekStream(nint handle, uint pos, nint userdata)
	{
		if (!(GCHandle.FromIntPtr(handle).Target is FileStream fileStream))
		{
			return Result.ErrInvalidHandle;
		}
		fileStream.Seek(pos, SeekOrigin.Begin);
		return Result.Ok;
	}

	public static void PlaySound(SoundBehavior soundBehavior, out IChannel? iChannel, bool startPaused = false)
	{
		Play2DSound(soundBehavior, out iChannel, 1f, startPaused);
	}

	public static void Play2DSound(SoundBehavior soundBehavior, out IChannel? iChannel, float volume, bool startPaused = false)
	{
		SoundBehavior soundBehavior2 = soundBehavior.Get();
		soundBehavior2.Play(default, volume, out iChannel, startPaused);
	}

	private static ClusterChannelWrapper? GetPlayingClusterChannel(KeyHash hash)
	{
		foreach (ClusterChannelWrapper clusterChannel in ClusterChannels)
		{
			if (clusterChannel.IsStopping || !(clusterChannel.Sound.Hash == hash))
			{
				continue;
			}
			return clusterChannel;
		}
		return null;
	}

	public static void PlaySound(SoundEvent soundEvent, SpatialAudio spatial, out IChannel? channelWrapper, IAudio? parent = null, float volume = 1f, bool startPaused = false)
	{
		channelWrapper = null;
		if (Universe.SimulationSpeed > 10.0)
		{
			return;
		}
		SoundBehavior soundBehavior = ModLibrary.Get<SoundBehavior>(soundEvent.SoundId);
		soundBehavior.Play(spatial, volume, out channelWrapper, startPaused);
		if (channelWrapper == null || soundBehavior.ClusterSound == null)
		{
			return;
		}
		ClusterChannelWrapper clusterChannelWrapper = ((parent != null) ? parent.GetPlayingClusterChannel(soundBehavior.ClusterSound.Hash) : GetPlayingClusterChannel(soundBehavior.ClusterSound.Hash));
		if (clusterChannelWrapper == null)
		{
			soundBehavior.ClusterSound.Play(spatial, volume, out IChannel iChannel, startPaused);
			clusterChannelWrapper = iChannel as ClusterChannelWrapper;
			if (clusterChannelWrapper != null)
			{
				if (parent != null)
				{
					parent.ClusterChannels.Add(clusterChannelWrapper);
				}
				else
				{
					ClusterChannels.Add(clusterChannelWrapper);
				}
			}
		}
		clusterChannelWrapper?.Subscribe(channelWrapper);
	}

	public static void PlaySound(SoundBehavior soundBehavior, SpatialAudio spatial, out IChannel? channelWrapper, float volume = 1f, bool startPaused = false, bool alwaysPlay = false)
	{
		channelWrapper = null;
		if (!(Universe.SimulationSpeed > 10.0) || alwaysPlay)
		{
			soundBehavior.Play(spatial, volume, out channelWrapper, startPaused);
		}
	}

	public static float MapVolumeSetting(int volumeSetting)
	{
		volumeSetting = int.Clamp(volumeSetting, 0, 100);
		if (volumeSetting == 0)
		{
			return 0f;
		}
		float amount = (float)volumeSetting / 100f;
		float value = -80f;
		float num = float.Lerp(value, 0f, amount);
		return float.Pow(10f, num / 40f);
	}

	public static void ApplySettings()
	{
		SetChannelGroupVolume(MasterGroup, MapVolumeSetting(GameSettings.Current.Audio.MasterVolume));
		SetChannelGroupVolume(SfxGroup, MapVolumeSetting(GameSettings.Current.Audio.SfxVolume));
		SetChannelGroupVolume(UiGroup, MapVolumeSetting(GameSettings.Current.Audio.UiVolume));
		SetChannelGroupVolume(MusicGroup, MapVolumeSetting(GameSettings.Current.Audio.MusicVolume));
	}

	public static void OnDrawUI()
	{
		if (!Program.ShowAudioDebug)
		{
			return;
		}
		DrawPlayingSoundPositions();
		ImGuiWindowFlags flags = ImGuiWindowFlags.NoSavedSettings;
		ImGui.SetNextWindowSize(ConsoleStyle.ConstrainNextWindowToScreen(new float2(480f, 820f)), ImGuiCond.Appearing);
		if (!ImGui.Begin("Audio Debug"u8, ref Program.ShowAudioDebug, flags))
		{
			return;
		}
		if (ImGui.IsWindowFocused() || ImGui.IsAnyItemActive() || ImGui.IsAnyItemHovered() || ImGui.IsWindowHovered())
		{
			Program.IsWindowOpen = true;
		}
		ImGui.Text("Previous Music: "u8);
		ImGui.Text(LocationMusicPlayer.PreviousChannel?.Sound?.Id ?? "None");
		ImGui.Text("Current Music: "u8);
		ImGui.SameLine();
		ImGui.Text(LocationMusicPlayer.CurrentChannel?.Sound?.Id ?? "None");
		Program.MainViewport.BiomeSoundController?.EnvironmentSound?.DrawDebug();
		ImGui.Separator();
		for (int i = 0; i < _biomeNames.Length; i++)
		{
			if (!string.IsNullOrEmpty(_biomeNames[i]))
			{
				ImGui.Text(_biomeNames[i]);
				ImGui.SameLine();
				ImGui.Text(" :"u8);
				ImGui.SameLine();
				ImGui.Text(_biomeValues[i].ToString());
			}
		}
		DrawPlayingSoundsSection();
		ImGui.End();
	}

	private static void DrawPlayingSoundsSection()
	{
		ImGui.Separator();
		ImGui.Text("Playing Sounds"u8);
		ImGui.PushStyleColor(ImGuiCol.Button, ShowSoundPositions ? Color.Green.AsByte4 : KSAColor.DarkGrey.AsByte4);
		ImGui.PushStyleColor(ImGuiCol.Text, Color.Black.AsByte4);
		if (ImGui.Button(ShowSoundPositions ? "Positions: ON"u8 : "Positions: OFF"u8, (float2?)null))
		{
			ShowSoundPositions = !ShowSoundPositions;
		}
		ImGui.PopStyleColor(2);
		int num = 0;
		foreach (IChannel activeChannel in ActiveChannels)
		{
			if (activeChannel != null && activeChannel.IsPlaying())
			{
				num++;
			}
		}
		ImGui.Text("Channels: "u8);
		ImGui.SameLine();
		ImGui.Text(num.ToString());
		ImGui.SameLine();
		ImGui.Text(" / "u8);
		ImGui.SameLine();
		ImGui.Text(ActiveChannels.Count.ToString());
		ImGui.SameLine();
		int num2 = 0;
		foreach (IAudio item in AllIAudio)
		{
			num2 += item.ClusterChannels.Count;
		}
		ImGui.Text(" active, clusters: "u8);
		ImGui.SameLine();
		ImGui.Text(ClusterChannels.Count.ToString());
		ImGui.SameLine();
		ImGui.Text(" global + "u8);
		ImGui.SameLine();
		ImGui.Text(num2.ToString());
		ImGui.SameLine();
		ImGui.Text(" local"u8);
		foreach (IChannel activeChannel2 in ActiveChannels)
		{
			if (activeChannel2 != null && activeChannel2.IsPlaying())
			{
				DrawPlayingSoundRow(activeChannel2);
			}
		}
		foreach (ClusterChannelWrapper clusterChannel in ClusterChannels)
		{
			if (clusterChannel.IsPlaying())
			{
				DrawPlayingSoundRow(clusterChannel);
			}
		}
		foreach (IAudio item2 in AllIAudio)
		{
			foreach (ClusterChannelWrapper clusterChannel2 in item2.ClusterChannels)
			{
				if (clusterChannel2.IsPlaying())
				{
					DrawPlayingSoundRow(clusterChannel2);
				}
			}
		}
	}

	private static void DrawPlayingSoundRow(IChannel channel)
	{
		double num = channel.SpatialAudio.Distance();
		bool flag = num < 0.001;
		ImGui.Text(channel.Sound?.Id ?? "None");
		ImGui.SameLine();
		if (flag)
		{
			ImGui.Text(" (2D)"u8);
			return;
		}
		ImGui.Text(" @ "u8);
		ImGui.SameLine();
		ImGui.Text(FormatDistance(num));
		if (channel.IsVirtual())
		{
			ImGui.SameLine();
			ImGui.Text(" (virtual)"u8);
		}
	}

	private static void DrawPlayingSoundPositions()
	{
		if (!ShowSoundPositions)
		{
			return;
		}
		IGameViewport mainViewport = Program.MainViewport;
		if (mainViewport == null)
		{
			return;
		}
		Camera camera = mainViewport.GetCamera();
		ImDrawListPtr mainOverlayDrawList = ImGuiHelper.GetMainOverlayDrawList();
		float2 position = mainViewport.Position;
		foreach (IChannel activeChannel in ActiveChannels)
		{
			if (activeChannel != null && activeChannel.IsPlaying())
			{
				DrawSoundPositionMarker(mainOverlayDrawList, camera, position, activeChannel, isCluster: false);
			}
		}
		foreach (IAudio item in AllIAudio)
		{
			foreach (ClusterChannelWrapper clusterChannel in item.ClusterChannels)
			{
				if (clusterChannel.IsPlaying())
				{
					DrawSoundPositionMarker(mainOverlayDrawList, camera, position, clusterChannel, isCluster: true);
				}
			}
		}
		foreach (ClusterChannelWrapper clusterChannel2 in ClusterChannels)
		{
			if (clusterChannel2.IsPlaying())
			{
				DrawSoundPositionMarker(mainOverlayDrawList, camera, position, clusterChannel2, isCluster: true);
			}
		}
	}

	private static void DrawSoundPositionMarker(ImDrawListPtr drawList, Camera camera, float2 viewportPos, IChannel channel, bool isCluster)
	{
		SpatialAudio spatialAudio = channel.SpatialAudio;
		double num = spatialAudio.Distance();
		if (num < 0.001)
		{
			return;
		}
		float2 float5 = camera.EgoToScreen(spatialAudio.PositionEgo);
		if (!float.IsNaN(float5.X) && !float.IsNaN(float5.Y))
		{
			byte4 value = Color.Cyan.AsByte4;
			if (isCluster)
			{
				value = Color.Magenta.AsByte4;
			}
			else if (channel.IsVirtual())
			{
				value = KSAColor.DarkGrey.AsByte4;
			}
			float2 center = viewportPos + float5;
			drawList.AddCircleFilled(in center, 4f, value);
			float2 screenPosition = center + new float2(8f, (0f - ImGui.GetTextLineHeight()) * 0.5f);
			ImGuiHelper.DrawTextOnScreen(drawList, screenPosition, channel.Sound?.Id ?? "None", value);
			screenPosition.Y += ImGui.GetTextLineHeight();
			ImGuiHelper.DrawTextOnScreen(drawList, screenPosition, FormatDistance(num), value);
		}
	}

	private static string FormatDistance(double metres)
	{
		if (!(metres < 1000.0))
		{
			return $"{metres * 0.001:F2} km";
		}
		return $"{metres:F1} m";
	}

	public static void SetBiomeDebugValue(scoped ReadOnlySpan<string> names, scoped ReadOnlySpan<float> values)
	{
		for (int i = 0; i < _biomeNames.Length; i++)
		{
			_biomeNames[i] = names[i];
			_biomeValues[i] = values[i];
		}
	}

	public static ChannelGroup GetChannelGroup(ChannelGroupType channelGroupType)
	{
		if (1 == 0)
		{
		}
		ChannelGroup result = channelGroupType switch
		{
			ChannelGroupType.Master => MasterGroup, 
			ChannelGroupType.Sfx => SfxGroup, 
			ChannelGroupType.Ui => UiGroup, 
			ChannelGroupType.Music => MusicGroup, 
			_ => MasterGroup, 
		};
		if (1 == 0)
		{
		}
		return result;
	}
}
