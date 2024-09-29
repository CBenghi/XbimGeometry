#nullable enable
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using Xbim.Common.Geometry;
using Xbim.Ifc4x3.GeometryResource;

namespace Xbim.ModelGeometry.Scene.LinearPlacement
{
	internal class LinearPlacementGradientCurve : ILinearPlacementCurve
	{
		public LinearPlacementGradientCurve(IfcGradientCurve curveGradient)
		{
			CurveGradient = curveGradient;
		}

		internal List<ILinearPlacementCurveSegment> BaseSegments { get; set; } = new List<ILinearPlacementCurveSegment>();
		public IfcGradientCurve CurveGradient { get; }

		internal XbimMatrix3D GetSegmentTransform(int segmentId, ILogger logger, out string segmentType)
		{
			var t = BaseSegments[segmentId];
			segmentType = (t is LinearPlacementCurveSegment lp) ? lp.Shape.GetType().Name : "unknown";
			return t.GetTransform(0, logger);
		}

		public XbimMatrix3D GetTransform(double distanceAlong, ILogger? logger)
		{
			double outstanding = distanceAlong;
			foreach (var segment in BaseSegments)
			{
				var segLen = segment.GetLen();
				if (outstanding > segLen)
				{
					outstanding -= segLen;
					continue;
				}
				// we are in the right segment
				return segment.GetTransform(outstanding, logger);
			}
			logger?.LogWarning("Distance along curve ({distanceAlong}) is greater than segments lenght in #{entityLabel}={entityType}. Entities might be misplaced.", distanceAlong, CurveGradient.EntityLabel, CurveGradient.GetType().Name);
			return XbimMatrix3D.Identity;
		}

		internal static LinearPlacementGradientCurve? GetCurve(IfcGradientCurve curveGradient, ILogger? logger = null)
		{
			var lpc = new LinearPlacementGradientCurve(curveGradient);
			if (curveGradient.BaseCurve is IfcCompositeCurve compCrv)
			{
				foreach (var seg in compCrv.Segments)
				{
					if (LinearPlacementCurve.TryGetCurve(seg, out var anyCurve, logger) && anyCurve is ILinearPlacementCurveSegment curveSegment)
					{
						lpc.BaseSegments.Add(curveSegment);
					}
					else
					{
						logger?.LogError("LinearPlacementCurve.TryGetCurve not implemented for #{entityLabel}={entType}", seg.EntityLabel, seg.GetType().Name);
						return null;
					}
				}
			}
			return lpc;
		}

		internal IEnumerable<double> GetBaseLenghts()
		{
			foreach (var segment in BaseSegments)
			{
				yield return segment.GetLen();
			}
		}

		
	}
}
#nullable restore