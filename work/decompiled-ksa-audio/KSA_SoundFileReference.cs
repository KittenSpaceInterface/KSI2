using System;
using System.IO;
using System.Xml.Serialization;
using Brutal.FmodApi;

namespace KSA;

public class SoundFileReference : FileReference, IKeyed
{
	[XmlAttribute("TwoD")]
	public bool IsTwoD = false;

	[XmlAttribute("StreamFromDisk")]
	public bool StreamFromDisk = false;

	[XmlElement("Loop")]
	public LoopData? LoopData;

	[XmlAttribute("Volume")]
	public float Volume = 1f;

	private const float MIN_VOLUME = 0f;

	private const float MAX_VOLUME = 2f;

	[XmlIgnore]
	public Sound FmodSound { get; private set; }

	public Mode Mode => BuildModeFlags();

	private Mode BuildModeFlags()
	{
		Mode mode = (IsTwoD ? Mode._2d : Mode._3d);
		mode = ((LoopData == null) ? (mode | Mode.LoopOff) : (mode | Mode.LoopNormal));
		if (StreamFromDisk)
		{
			mode |= Mode.NonBlocking;
		}
		return mode;
	}

	public override bool IsLoaded()
	{
		return !IsReference();
	}

	public override SoundFileReference Get()
	{
		return IsReference() ? ModLibrary.Get<SoundFileReference>(Id) : this;
	}

	public override SoundFileReference Populate()
	{
		if (IsReference())
		{
			return ModLibrary.Get<SoundFileReference>(Id) ?? this;
		}
		return this;
	}

	internal override void DoLoad()
	{
		if (Mod == null)
		{
			throw new NullReferenceException("Mod is null for sound file " + ModPath);
		}
		if (string.IsNullOrEmpty(LocalPath))
		{
			throw new NullReferenceException($"LocalPath is null or empty for Sound in '{Mod}'");
		}
		if (!File.Exists(ModPath))
		{
			throw new NullReferenceException("Sound file does not exist in '" + ModPath + "'");
		}
		if (!GameAudio.CreateFmodSound(this, out var sound, out var result))
		{
			throw new FmodResultException(result.ToString());
		}
		FmodSound = sound;
		Volume = float.Clamp(Volume, 0f, 2f);
	}
}
