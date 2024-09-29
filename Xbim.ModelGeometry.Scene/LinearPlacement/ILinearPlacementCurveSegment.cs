#nullable enable

namespace Xbim.ModelGeometry.Scene.LinearPlacement
{
	internal interface ILinearPlacementCurveSegment : ILinearPlacementCurve
	{
		public double GetLen();
	}
}
#nullable restore