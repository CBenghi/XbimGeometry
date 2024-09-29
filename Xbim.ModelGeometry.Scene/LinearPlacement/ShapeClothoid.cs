#nullable enable
using Microsoft.Extensions.Logging;
using System;
using System.Transactions;
using Xbim.Common.Geometry;
using Xbim.Ifc4x3.GeometryResource;
using Xbim.Ifc4x3.MeasureResource;

namespace Xbim.ModelGeometry.Scene.LinearPlacement
{
	public class ShapeClothoid : IShape
	{
		IfcClothoid clothoid;
		public ShapeClothoid(IfcClothoid clothoid)
		{
			this.clothoid = clothoid;
		}

		public XbimMatrix3D GetTransform(IfcCurveMeasureSelect segmentStart, double distanceAlong, ILogger? logger)
		{
			if (segmentStart is not IfcLengthMeasure lm || lm.Value is not double segStartValue)
			{
				return XbimMatrix3D.Identity;
			}
			if (clothoid is null)
				return XbimMatrix3D.Identity;
			var cConstant = (clothoid.ClothoidConstant.Value is double d) ? d : 0.0;
			if (segStartValue == 0 && distanceAlong == 0.0)
				return XbimMatrix3D.Identity;
			var step = Math.Min(clothoid.Model.ModelFactors.OneMeter, distanceAlong / 10);
			GetCoreClothoid(cConstant, segStartValue, step, out var startPosX, out var startPosY, out var startAngle);
			GetCoreClothoid(cConstant, segStartValue + distanceAlong, step, out var endPosX, out var endPosY, out var endAngle);
			return GetTransform(
				startPosX, startPosY, startAngle,
				endPosX, endPosY, endAngle
				);
		}


		public static XbimMatrix3D GetTransform(double originPosX, double originPosY, double originAngle, double locationPosX, double locationPosY, double locationAngle)
		{
			var mt = XbimMatrix3D.CreateTranslation(-originPosX, -originPosY, 0);
			XbimMatrix3D rot = CreateRotationZ(-originAngle);
			var t = mt * rot;
			var locationPoint = new XbimPoint3D(locationPosX, locationPosY, 0);
			var locationRelativeToOrigin = locationPoint * t;
			var refTrasl = XbimMatrix3D.CreateTranslation(locationRelativeToOrigin.X, locationRelativeToOrigin.Y, 0);
			var newAngle = locationAngle - originAngle;
			var rot2 = CreateRotationZ(newAngle);
			var tret = rot2 * refTrasl;
			return tret;
		}

		public static XbimMatrix3D CreateRotationZ(double theta)
		{
			var ret = new XbimMatrix3D
			{
				M11 = Math.Cos(theta),
				M21 = -Math.Sin(theta),
				M12 = Math.Sin(theta),
				M22 = Math.Cos(theta)
			};
			return ret;
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

		public double GetParamLen(double v)
		{
			throw new NotImplementedException();
		}
	}
}
#nullable restore