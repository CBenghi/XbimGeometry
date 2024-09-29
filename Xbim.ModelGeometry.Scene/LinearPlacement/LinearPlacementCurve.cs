#nullable enable
using Microsoft.Extensions.Logging;
using Microsoft.Isam.Esent.Interop;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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

	public static class LinearPlacementCurve
	{
		internal static bool TryGetCurve(IPersistEntity ifcCurve, [NotNullWhen(true)] out ILinearPlacementCurve? curve, ILogger? logger = null)
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
}
#nullable restore