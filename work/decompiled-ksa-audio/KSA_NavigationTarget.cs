using System;
using Brutal.Numerics;

namespace KSA;

public struct NavigationTarget
{
	public required doubleQuat Dock2Cci;

	public required double3 DockFrameRatesCci;

	public required double3 PositionCci;

	public required double3 VelocityCci;

	public static NavigationTarget? Create(IOrbiter? target, Part? targetPart, IParentBody myParent, UniverseTime time)
	{
		if (target == null)
		{
			return null;
		}
		doubleQuat cce2Cci = myParent.GetCce2Cci();
		StateVectors stateVectorsAt = target.Orbit.GetStateVectorsAt(time);
		double3 double5;
		double3 velocityCci;
		if (target.Parent == myParent)
		{
			double5 = stateVectorsAt.PositionCci;
			velocityCci = stateVectorsAt.VelocityCci;
		}
		else
		{
			double3 positionEcl;
			double3 velocityEcl;
			if (myParent is IOrbiter orbiter)
			{
				positionEcl = orbiter.GetPositionEcl(time);
				velocityEcl = orbiter.GetVelocityEcl(time);
			}
			else
			{
				positionEcl = myParent.GetPositionEcl();
				velocityEcl = myParent.GetVelocityEcl();
			}
			double3 positionEcl2;
			double3 velocityEcl2;
			if (target.Parent is IOrbiter orbiter2)
			{
				positionEcl2 = orbiter2.GetPositionEcl(time);
				velocityEcl2 = orbiter2.GetVelocityEcl(time);
			}
			else
			{
				positionEcl2 = target.Parent.GetPositionEcl();
				velocityEcl2 = target.Parent.GetVelocityEcl();
			}
			doubleQuat cci2Cce = target.Parent.GetCci2Cce();
			double3 double6 = positionEcl2 + stateVectorsAt.PositionCci.Transform(cci2Cce);
			double3 value = double6 - positionEcl;
			double5 = value.Transform(cce2Cci);
			double3 double7 = velocityEcl2 + stateVectorsAt.VelocityCci.Transform(cci2Cce);
			double3 value2 = double7 - velocityEcl;
			velocityCci = value2.Transform(cce2Cci);
		}
		doubleQuat doubleQuat2 = doubleQuat.Concatenate(target.GetBodyFixed2Ecl(), cce2Cci);
		double3 bodyRates = target.GetBodyRates();
		doubleQuat targetPart2Body = doubleQuat.Identity;
		double3 value3 = double3.Zero;
		if (targetPart != null)
		{
			Span<DockingPort> span = targetPart.SubtreeModules.Get<DockingPort>();
			if (span.Length > 0)
			{
				Part.Connector connector = span[0].Connector;
				targetPart2Body = connector.Asmb2VehicleAsmb;
				Vehicle owningVehicle = targetPart.Tree.OwningVehicle;
				if (owningVehicle != null)
				{
					value3 = connector.PositionVehicleAsmb - owningVehicle.CenterOfMassAsmb;
				}
			}
		}
		return new NavigationTarget
		{
			Dock2Cci = VehicleReferenceFrameEx.GetDock2Cci(doubleQuat2, targetPart2Body),
			DockFrameRatesCci = VehicleReferenceFrameEx.GetDockFrameRatesCci(doubleQuat2, bodyRates),
			PositionCci = double5 + value3.Transform(doubleQuat2),
			VelocityCci = velocityCci
		};
	}
}
