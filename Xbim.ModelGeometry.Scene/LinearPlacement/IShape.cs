#nullable enable
using Microsoft.Extensions.Logging;
using Xbim.Common.Geometry;
using Xbim.Ifc4x3.GeometryResource;

namespace Xbim.ModelGeometry.Scene.LinearPlacement
{
	internal interface IShape
	{
		public XbimMatrix3D GetTransform(IfcCurveMeasureSelect segmentStart, double distanceAlong, ILogger? logger);
		double GetParamLen(double v);
	}
}
#nullable restore