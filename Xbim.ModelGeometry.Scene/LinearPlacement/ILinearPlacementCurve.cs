#nullable enable
using Microsoft.Extensions.Logging;
using Xbim.Common.Geometry;

namespace Xbim.ModelGeometry.Scene.LinearPlacement
{
	internal interface ILinearPlacementCurve
	{
		public XbimMatrix3D GetTransform(double distanceAlong, ILogger? logger = null);
	}
}
#nullable restore