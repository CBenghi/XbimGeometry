#nullable enable
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using System;
using System.Diagnostics;
using Xbim.Common.Geometry;
using Xbim.Ifc.Extensions;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4x3.GeometryResource;
using Xbim.Ifc4x3.MeasureResource;

namespace Xbim.ModelGeometry.Scene.LinearPlacement
{
	[DebuggerDisplay("{Shape.GetType().Name}")]
	internal class LinearPlacementCurveSegment : ILinearPlacementCurveSegment
	{
		public LinearPlacementCurveSegment(IfcCurveMeasureSelect segmentStart, IfcCurveMeasureSelect segmentLength, XbimMatrix3D placement, IShape shape)
		{
			SegStart = segmentStart;
			SegLen = segmentLength;
			Placement = placement;
			Shape = shape;
		}

		internal IShape Shape;
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
			var computedMat = curveSegment.Placement.ToMatrix3dBugFix(true);
			var ret = new LinearPlacementCurveSegment
			(
				curveSegment.SegmentStart,
				curveSegment.SegmentLength,
				computedMat,
				shape
			);
			return ret;
		}

		internal static double BareDoubleValue(IfcCurveMeasureSelect segmentStart)
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
				return BareDoubleValue(SegLen);
			if (SegLen is IfcParameterValue pval)
				return Shape.GetParamLen(BareDoubleValue(SegLen));
			return 0;
		}

		public XbimMatrix3D GetTransform(double distanceAlong, ILogger? logger)
		{
			// var d = BareDoubleValue(SegStart);
			var loctransf = Shape.GetTransform(SegStart, distanceAlong, logger);
			// Debug.WriteLine($"Localtransfer: {XbimPlacementTree.XbimPlacementNode.Summarize(loctransf)}");
			// Debug.WriteLine($"Placement: {XbimPlacementTree.XbimPlacementNode.Summarize(Placement)}");
			var computed = loctransf * Placement;
			// Debug.WriteLine($"computed: {XbimPlacementTree.XbimPlacementNode.Summarize(computed)}");
			return computed;
		}
	}
}
#nullable restore