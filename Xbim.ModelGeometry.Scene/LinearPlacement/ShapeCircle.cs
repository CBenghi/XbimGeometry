#nullable enable
using Microsoft.Extensions.Logging;
using System;
using Xbim.Common.Geometry;
using Xbim.Ifc.Extensions;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4x3.GeometryResource;

namespace Xbim.ModelGeometry.Scene.LinearPlacement
{
	internal class ShapeCircle : IShape
	{
		IfcCircle _circle;
		public ShapeCircle(IfcCircle circle)
		{
			_circle = circle;
		}

		public double GetParamLen(double v)
		{
			if (_circle.Radius.Value is double d)
				return Math.Abs(d * v);
			return 0;
		}

		public XbimMatrix3D GetTransform(IfcCurveMeasureSelect segmentStart, double distanceAlong, ILogger? logger)
		{
			return ToMatrix3dBugFix(_circle.Position);
		}

		private XbimMatrix3D ToMatrix3dBugFix(IfcAxis2Placement position)
		{
			if (position is IIfcAxis2Placement2D plc)
				return plc.ToMatrix3dBugFix();
			return position.ToMatrix3D();
		}
	}
}
#nullable restore