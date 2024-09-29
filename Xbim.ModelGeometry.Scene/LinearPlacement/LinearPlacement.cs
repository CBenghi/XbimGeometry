#nullable enable
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xbim.Common.Geometry;
using Xbim.Ifc.Extensions;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4x3.GeometryResource;
using Xbim.Ifc4x3.MeasureResource;

namespace Xbim.ModelGeometry.Scene.LinearPlacement
{
	public static class LinearPlacement
	{
		public static XbimMatrix3D ToMatrix3dBugFix(this IIfcPlacement placement, bool noRot = false)
		{
			if (placement is IIfcAxis2Placement2D axis2)
			{
				if (axis2.RefDirection != null)
				{
					var v = new XbimVector3D(axis2.RefDirection.X, axis2.RefDirection.Y, 0);
					v = v.Normalized();
					//if (noRot)
					//	v = new XbimVector3D(1, 0, 0);
					return new XbimMatrix3D(
						v.X, -v.Y, 0, 0,
						v.Y, v.X, 0, 0,
						0, 0, 1, 0,
						axis2.Location.X, axis2.Location.Y, 0, 1);
					//return new XbimMatrix3D(
					//	v.X, v.Y, 0, 0,
					//	-v.Y, v.X, 0, 0,
					//	0, 0, 1, 0,
					//	axis2.Location.X, axis2.Location.Y, 0, 1);
				}
				return new XbimMatrix3D(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, axis2.Location.X, axis2.Location.Y, axis2.Location.Z, 1);
			}
			return placement.ToMatrix3D();
		}

		public static bool TryGetPlacement(Ifc4x3.GeometricConstraintResource.IfcLinearPlacement linPlacement, out XbimMatrix3D placementMatrix, ILogger? logger)
		{
			var rel = XbimMatrix3D.Identity;
			if (linPlacement.PlacementRelTo != null)
			{
				rel = linPlacement.PlacementRelTo.ToMatrix3D();
			}
			if (linPlacement.CartesianPosition != null)
			{
				placementMatrix = rel * linPlacement.CartesianPosition.ToMatrix3D();
				return true;
			}
			if (linPlacement.RelativePlacement is not null)
			{
				var fnd = TryGetMatrix(linPlacement.RelativePlacement, out var m, logger);
				if (fnd)
				{
					placementMatrix = rel * m;
					return true;
				}
				placementMatrix = XbimMatrix3D.Identity;
				return true;
			}
			placementMatrix = XbimMatrix3D.Identity;
			return false;
		}

		private static bool TryGetMatrix(IfcAxis2PlacementLinear placementLinear, out XbimMatrix3D m, ILogger? logger)
		{
			if (placementLinear.Axis != null || placementLinear.RefDirection != null) 
				logger?.LogWarning("Axis and RefDirection are not considered in IfcAxis2PlacementLinear.TryGetMatrix()");
			if (placementLinear.Location is IfcPointByDistanceExpression pointByDistance)
			{
				return TryGetMatrix(pointByDistance, out m, logger);
			}
			else
			{
				m = XbimMatrix3D.Identity;
				return false;
			}
		}

		private static bool TryGetMatrix(IfcPointByDistanceExpression pointByDistance, out XbimMatrix3D m, ILogger? logger)
		{
			var canGetCurve = LinearPlacementCurve.TryGetCurve(pointByDistance.BasisCurve, out var curve, logger);
			if (canGetCurve)
			{
				if (pointByDistance.DistanceAlong is IfcLengthMeasure lm && lm.Value is double d)
				{
					m = curve!.GetTransform(d, logger);
					return true;
				}
			}
			m = XbimMatrix3D.Identity;
			return false;
		}
	}
}
#nullable restore