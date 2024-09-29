#nullable enable
using Microsoft.Extensions.Logging;
using System;
using Xbim.Common.Geometry;
using Xbim.Ifc4x3.GeometryResource;

namespace Xbim.ModelGeometry.Scene.LinearPlacement
{
	internal class ShapeLine : IShape
	{
		IfcLine line;
		public ShapeLine(IfcLine ln)
		{
			line = ln;
		}

		public double GetParamLen(double v)
		{
			throw new NotImplementedException();
		}

		public XbimMatrix3D GetTransform(IfcCurveMeasureSelect segmentStart, double distanceAlong, ILogger? logger)
		{
			if (LinearPlacementCurveSegment.BareDoubleValue(segmentStart) != 0)
			{
				logger?.LogError("segmentStart is ignored in ShapeLine implementation.");
			}
			XbimVector3D dirVector = new XbimVector3D(
								line.Dir.Orientation.X,
								line.Dir.Orientation.Y,
								double.IsNaN(line.Dir.Orientation.Z) ? 0 : line.Dir.Orientation.Z);
			var rotationAtPoint = XbimMatrix3D.CreateRotation(
				new XbimPoint3D(
					dirVector.X,
					dirVector.Y,
					dirVector.Z),
				new XbimPoint3D(
					1, 
					0, 
					0)
				);
			var startPos = new XbimPoint3D(
				line.Pnt.X,
				line.Pnt.Y,
				double.IsNaN(line.Pnt.Z) ? 0 : line.Pnt.Z
				);
			var offset = (distanceAlong * dirVector);
			var translationMatrix = XbimMatrix3D.CreateTranslation(
				offset.X + startPos.X,
				offset.Y + startPos.Y,
				offset.Z + startPos.Z
				);

			return rotationAtPoint * translationMatrix;
		}
	}
}
#nullable restore