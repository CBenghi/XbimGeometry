#nullable enable
using Microsoft.Extensions.Logging;
using Microsoft.Isam.Esent.Interop;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Xbim.Common;
using Xbim.Common.Geometry;
using Xbim.Ifc.Extensions;
using Xbim.Ifc4x3.GeometryResource;
using Xbim.Ifc4x3.MeasureResource;

namespace Xbim.ModelGeometry.Scene.LinearPlacement
{
	internal interface IShape
	{
		public XbimMatrix3D GetTransform(double segmentStart, double distanceAlong);
	}

	public class ShapeClothoid : IShape
	{
		IfcClothoid clothoid;
		public ShapeClothoid(IfcClothoid clothoid)
		{
			this.clothoid = clothoid;
		}

		public XbimMatrix3D GetTransform(double segmentStart, double distanceAlong)
		{
			if (clothoid is null)
				return XbimMatrix3D.Identity;
			var cConstant = (clothoid.ClothoidConstant.Value is double d) ? d : 0.0;
			if (segmentStart == 0 && distanceAlong == 0.0)
				return XbimMatrix3D.Identity;
			var step = Math.Min(clothoid.Model.ModelFactors.OneMeter, distanceAlong / 10);
			GetCoreClothoid(cConstant, segmentStart, step, out var startPosX, out var startPosY, out var startAngle);
			GetCoreClothoid(cConstant, segmentStart + distanceAlong, step, out var endPosX, out var endPosY, out var endAngle);
			return GetTransform(
				startPosX, startPosY, startAngle,
				endPosX, endPosY, endAngle
				);
		}

		public static XbimMatrix3D GetTransform2(double originPosX, double originPosY, double originAngle, double locationPosX, double locationPosY, double locationAngle)
		{
			// Calculate the rotation matrices for both coordinate systems
			XbimMatrix3D rotation1 = CreateRotationZ(originAngle);
			XbimMatrix3D rotation2 = CreateRotationZ(locationAngle);

			// Calculate the translation matrices for both coordinate systems
			XbimMatrix3D translation1 = XbimMatrix3D.CreateTranslation(new XbimVector3D(originPosX, originPosY, 0));
			XbimMatrix3D translation2 = XbimMatrix3D.CreateTranslation(new XbimVector3D(locationPosX, locationPosY, 0));

			// Calculate the final transformation matrix from system 1 to system 2
			// Inverse transform of the first system (origin1 and rotation1)
			XbimMatrix3D inverseTranslation1 = XbimMatrix3D.CreateTranslation(new XbimVector3D(-originPosX, -originPosY, 0));
			XbimMatrix3D inverseRotation1 = CreateRotationZ(originAngle);
			inverseRotation1.Invert();
			
			// Combine all transformations
			XbimMatrix3D transformationMatrix = inverseRotation1 * inverseTranslation1 * translation2 * rotation2;

			return transformationMatrix;
		}

		public static XbimMatrix3D GetTransform(double originPosX, double originPosY, double originAngle, double locationPosX, double locationPosY, double locationAngle)
		{
			var mt = XbimMatrix3D.CreateTranslation(-originPosX, -originPosY, 0);
			XbimMatrix3D rot = CreateRotationZ(-originAngle);
			var t = mt * rot;
			var locationPoint = new XbimVector3D(locationPosX, locationPosY, 0);
			var locationRelativeToOrigin = locationPoint * t;
			var refTrasl = XbimMatrix3D.CreateTranslation(locationRelativeToOrigin);
			var newAngle = locationAngle - originAngle;
			var rot2 = CreateRotationZ(newAngle);
			var tret = rot2 * refTrasl;
			return tret;
		}

		public static XbimMatrix3D CreateRotationZ(double theta)
		{
			XbimMatrix3D ret = new XbimMatrix3D();
			ret.M11 = Math.Cos(theta);
			ret.M21 = -Math.Sin(theta);
			ret.M12 = Math.Sin(theta);
			ret.M22 = Math.Cos(theta);
			return ret;
		}

		private static XbimMatrix3D GetTransformFromAngle(double originAngle)
		{
			var angDir = new XbimPoint3D(Math.Cos(originAngle), Math.Sin(originAngle), 0);
			var rot = XbimMatrix3D.CreateRotation(
				angDir,
				new XbimPoint3D(1, 0, 0)
				);
			return rot;
		}

		public static void GetCoreClothoid(double clothoidConstant, double positionAlong, double stepSize, out double posX, out double posY, out double angle)
		{
			var N = (int)Math.Ceiling(Math.Abs(positionAlong) / stepSize);
			var deltaS = positionAlong / N;
			// determining direction
			var lambda = (positionAlong < 0) ? -1 : 1;

			double prevS = 0;
			posX = 0.0;
			posY = 0.0;
			angle = 0;
			for (int i = 0; i < N; i++)
			{
				angle = lambda * (Math.Pow(prevS, 2)) / (2 * Math.Pow(clothoidConstant, 2));
				double dx = deltaS * Math.Cos(angle);
				double dy = deltaS * Math.Sin(angle);
				posX += dx;
				posY += dy;
				prevS += deltaS;
			}
			if (clothoidConstant < 0)
			{ 
				posY *= -1;
				angle *= -1;
			}
			if (positionAlong < 0)
			{
				posY *= -1;
			}
		}
	}

	internal class ShapeLine : IShape
	{
		IfcLine line;
		public ShapeLine(IfcLine ln)
		{
			line = ln;
		}

		public XbimMatrix3D GetTransform(double segmentStart, double distanceAlong)
		{
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
				line.Pnt.Z
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

	internal class ShapeCircle : IShape
	{
		IfcCircle _circle;
		public ShapeCircle(IfcCircle circle)
		{
			_circle = circle;
		}

        public XbimMatrix3D GetTransform(double segmentStart, double distanceAlong)
		{
			return _circle.Position.ToMatrix3D();
		}
	}

	internal class LinearPlacementCurveSegment : ILinearPlacementCurveSegment
	{
		public LinearPlacementCurveSegment(IfcCurveMeasureSelect segmentStart, IfcCurveMeasureSelect segmentLength, XbimMatrix3D placement, IShape shape)
		{
			SegStart = segmentStart;
			SegLen = segmentLength;
			Placement = placement;
			Shape = shape;
		}

		IShape Shape;
		XbimMatrix3D Placement = XbimMatrix3D.Identity;
		IfcCurveMeasureSelect SegStart;
		IfcCurveMeasureSelect SegLen;
		internal static ILinearPlacementCurve? GetCurve(IfcCurveSegment curveSegment, ILogger? logger)
		{
			IShape? shape = curveSegment.ParentCurve switch
			{
				IfcCircle c => new ShapeCircle(c),
				IfcLine ln => new ShapeLine(ln),
				IfcClothoid ln => new ShapeClothoid(ln),
				_ => null
			};
			if (shape is null)
				return null;
			var ret = new LinearPlacementCurveSegment
			(
				curveSegment.SegmentStart,
				curveSegment.SegmentLength,
				curveSegment.Placement.ToMatrix3D(),
				shape
			);
			return ret;
		}

		private static double GetDouble(IfcCurveMeasureSelect segmentStart)
		{
			if (segmentStart == null)
				return 0;
			if (segmentStart is IfcLengthMeasure lm && lm.Value is double dv)
				return dv;
			else if (segmentStart is IfcParameterValue pv && pv.Value is double dpv)
				return dpv;
			return 0;
		}

		public double GetLen()
		{
			if (SegLen is IfcLengthMeasure)
				return GetDouble(SegLen);
			return 0;
		}

		public XbimMatrix3D GetTransform(double distanceAlong, ILogger? logger)
		{
			var d = GetDouble(SegStart);
			return Placement * Shape.GetTransform(d, distanceAlong);
		}
	}

	internal class LinearPlacementGradientCurve : ILinearPlacementCurve
	{
		internal List<ILinearPlacementCurveSegment> BaseSegments { get; set; } = new List<ILinearPlacementCurveSegment>();
		public XbimMatrix3D GetTransform(double distanceAlong, ILogger? logger)
		{
			double outstanding = distanceAlong;
			foreach (var segment in BaseSegments)
			{
				if (outstanding >= segment.GetLen())
				{
					outstanding -= segment.GetLen();
					continue;
				}
				// we are in the right segment
				return segment.GetTransform(outstanding, logger);
			}
			throw new NotImplementedException();
		}

		internal static LinearPlacementGradientCurve? GetCurve(IfcGradientCurve curveGradient, ILogger? logger = null)
		{
			var lpc = new LinearPlacementGradientCurve();
			foreach (var seg in curveGradient.Segments)
			{
				if (LinearPlacementCurve.TryGetCurve(seg, out var anyCurve, logger) && anyCurve is ILinearPlacementCurveSegment curveSegment)
				{
					lpc.BaseSegments.Add(curveSegment);
				}
				else
				{
					return null;
				}
			}
			return lpc;
		}
	}

	public static class LinearPlacementCurve
	{
		internal static bool TryGetCurve(IPersistEntity ifcCurve,[NotNullWhen(true)] out ILinearPlacementCurve? curve, ILogger? logger = null)
		{
			if (ifcCurve is null)
			{
				logger?.LogError("Invalid null parameter in LinearPlacementCurve.TryGetCurve.");
				curve = null;
				return false;
			}
			if (ifcCurve is IfcGradientCurve curveGradient)
			{
				curve = LinearPlacementGradientCurve.GetCurve(curveGradient, logger);
				return curve is not null;
			}
			if (ifcCurve is IfcCurveSegment curveSegment)
			{
				curve = LinearPlacementCurveSegment.GetCurve(curveSegment, logger);
				return curve is not null;
			}
			logger?.LogError("LinearPlacementCurve.TryGetCurve not implemented for {type}.", ifcCurve.GetType().Name);
			curve = null;
			return false;
		}
	}

	internal interface ILinearPlacementCurveSegment : ILinearPlacementCurve
	{
		public double GetLen();
	}

	internal interface ILinearPlacementCurve
	{
		public XbimMatrix3D GetTransform(double distanceAlong, ILogger? logger);
	}
}
#nullable restore