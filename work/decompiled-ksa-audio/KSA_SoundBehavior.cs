using System;
using System.Xml.Serialization;

namespace KSA;

public class SoundBehavior : SerializedId, IKeyed
{
	[XmlElement("ClusterSound")]
	public ClusterSound? ClusterSound;

	[XmlIgnore]
	public int LookupIndex { get; set; }

	public virtual void Play(SpatialAudio spatial, float volume, out IChannel? iChannel, bool startPaused = false)
	{
		iChannel = null;
	}

	public override bool IsReference()
	{
		return true;
	}

	public virtual SoundBehavior Get()
	{
		return IsReference() ? ModLibrary.Get<SoundBehavior>(Id) : this;
	}

	public override SoundBehavior Populate()
	{
		throw new NotImplementedException();
	}

	public override TableString.Row ToRow()
	{
		throw new NotImplementedException();
	}
}
