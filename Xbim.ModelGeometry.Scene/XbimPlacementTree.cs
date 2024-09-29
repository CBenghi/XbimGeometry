using Microsoft.Extensions.Logging;
using Microsoft.Isam.Esent.Interop;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using Xbim.Common;
using Xbim.Common.Geometry;
using Xbim.Geometry.Engine.Interop;
using Xbim.Ifc4.Interfaces;
using Xbim.ModelGeometry.Scene.Extensions;

#nullable enable

namespace Xbim.ModelGeometry.Scene
{
    public class XbimPlacementTree
    {
        /// <summary>
        /// This function centralises the extraction of a product placement, but it needs the support of XbimPlacementTree and an XbimGeometryEngine
        /// We should probably find a conceptual place for it somewhere in the scene, where these are cached.
        /// </summary>
        public static XbimMatrix3D GetTransform(IIfcProduct product, XbimPlacementTree tree, IXbimGeometryEngine engine, ILogger? logger = null)
        {
            _ = tree.TryGetTransform(product.ObjectPlacement, out var result, engine, logger);
            return result;
        }

        internal bool TryGetTransform(IIfcObjectPlacement objectPlacement, out XbimMatrix3D found, IXbimGeometryEngine? engine = null, ILogger? logger = null)
        {
            if (objectPlacement is not null)
            {
                if (Nodes.TryGetValue(objectPlacement.EntityLabel, out var node))
                {
                    found = node!.Matrix;
                    return true;
                }
                else
                {
					if (engine is null)
                    {
						found = XbimMatrix3D.Identity;
						return false;
					}
                    //	placementTransform = tree[product.ObjectPlacement.EntityLabel];
                    else
                    {
                        var newNode = new XbimPlacementNode(objectPlacement, logger, engine);
                        Nodes.Add(objectPlacement.EntityLabel, newNode);
						// todo: we should set the parent nodes before converting to global! 
						newNode.ToGlobalMatrix();
                        found = newNode.Matrix;
                        return true;
                    }
				}
            }
            else
            {
                found = XbimMatrix3D.Identity;
                return false;
            }
        }


        /// <summary>
        ///     Builds a placement tree of all ifcLocalPlacements
        /// </summary>
        /// <param name="model"></param>
        /// <param name="adjustWcs">
        ///     If there is a single root displacement, this is removed from the tree and added to the World
        ///     Coordinate System. Useful for models where the site has been located into a geographical context
        /// </param>
        /// <param name="logger">optional logging target</param>
        /// <param name="engine">The geometry engine is needed to compute some of the most complex placementss</param>
        public XbimPlacementTree(IModel model, bool adjustWcs = true, ILogger? logger = null, IXbimGeometryEngine? engine = null)
        {
            var rootNodes = new List<XbimPlacementNode>();
            var objectPlacements = model.Instances.OfType<IIfcObjectPlacement>(true).ToList();

            // populate the nodes
            Nodes = new Dictionary<int, XbimPlacementNode>();
            foreach (var placement in objectPlacements)
            {
                var tmp = new XbimPlacementNode(placement, logger, engine);
                Nodes.Add(placement.EntityLabel, tmp);
            }

            // traverse the nodes to complete their initialization, they are either root or not
            foreach (var objPlacement in objectPlacements)
            {
                if (TryGetPlacementRelTo(objPlacement, out var relPlacement))
                {
                    var xbimPlacement = Nodes[objPlacement.EntityLabel];
                    if (Nodes.TryGetValue(relPlacement.EntityLabel, out var relTo))
                    {
                        var xbimPlacementParent = relTo;
                        xbimPlacement.Parent = xbimPlacementParent;
                        xbimPlacementParent.Children.Add(xbimPlacement);
                    }
                    else
                    {
                        logger?.LogError("PlacementRelTo entity #{RelToEntityLabel} not found; adding #{PlacedEntity} as root node.", relPlacement.EntityLabel, objPlacement.EntityLabel);
                        rootNodes.Add(Nodes[objPlacement.EntityLabel]);
                    }
                }
                else
                    rootNodes.Add(Nodes[objPlacement.EntityLabel]);
            }

            // if we only have one root node, then we set the WorldCoordinateSystem and remove that node
            //
            var topNodes = rootNodes.ToList();
            if (adjustWcs && topNodes.Count == 1)
            {
                var root = topNodes[0];
                WorldCoordinateSystem = root.Matrix;
				// set the root matrix to identity
				root.Matrix = XbimMatrix3D.Identity;

                // todo: we could flatten te subsequent transform if still just one, changing this if into a while loop, 
                // but we first need to workout how to build the cumulative transform
                // would it be a or b?
                // a) wcs = wcs * newComponent;
                // b) wcs = newComponent * wcs; <- probably the right one, but it needs testing

                //make its children parentless // this probably would not be needed
				foreach (var node in Nodes.Values.Where(node => node.Parent == root))
                    node.Parent = null;
            }
            // muliply out the matrices
            foreach (var node in Nodes.Values)
                node.ToGlobalMatrix();
        }

        private static bool TryGetPlacementRelTo(IIfcObjectPlacement objPlacement, [NotNullWhen(true)] out IIfcObjectPlacement? relPlacement)
        {
            relPlacement = objPlacement switch
            {
                Ifc4x3.GeometricConstraintResource.IfcObjectPlacement objPlac4xc3 => objPlac4xc3.PlacementRelTo, // PlacementRelTo has been moved to the supertype in Ifc4x3
                IIfcLocalPlacement interfaceLocalPlacement => interfaceLocalPlacement.PlacementRelTo,
                _ => null,
            };
            return relPlacement is not null;
        }

        public XbimMatrix3D WorldCoordinateSystem { get; private set; }

        private Dictionary<int, XbimPlacementNode> Nodes { get; set; }

        public XbimMatrix3D this[int placementLabel]
        {
            get { return Nodes[placementLabel].Matrix; }
        }

        [DebuggerDisplay("{ShortDescription}")]
        public class XbimPlacementNode
        {
            public string ShortDescription
            {
                get
                {
                    StringBuilder sb = new StringBuilder();
                    sb.Append(Matrix.IsIdentity ? "Id" : Summarize(Matrix));
                    sb.Append($" #Children: {Children.Count}");
                    return sb.ToString();
                }
            }

			public static string Summarize(XbimMatrix3D matrix)
			{

                if (TryGetZRot(matrix, out double angleRadians))
                    return $"Rotation: {angleRadians * 180 / Math.PI:#.##}° X: {matrix.OffsetX:#.##}, Y: {matrix.OffsetY:#.##}, Z: {matrix.OffsetZ:#.##}";
                return $"Complex, X: {matrix.OffsetX:#.##}, Y: {matrix.OffsetY:#.##}, Z: {matrix.OffsetZ:#.##}";
			}

			private static bool TryGetZRot(XbimMatrix3D matrix, out double angleRadians)
			{
                angleRadians = 0 ;
				if (IsNotClose(matrix.M33, 1) || IsNotClose(matrix.M44, 1))
                    return false;
                if (IsNotClose(matrix.M31, 0) || IsNotClose(matrix.M32, 0) || IsNotClose(matrix.M13, 0) || IsNotClose(matrix.M23, 0))
                    return false;
				if (IsNotClose(matrix.M14 , 0) || IsNotClose(matrix.M24, 0) || IsNotClose(matrix.M34, 0))
					return false;
                if (IsNotClose(matrix.M11, matrix.M22))
                    return false;

				/*
                
                M11 = Math.Cos(theta),
				M21 = -Math.Sin(theta),
				M12 = Math.Sin(theta),
				M22 = Math.Cos(theta)
                 
                 */
				var ang = Math.Atan2(matrix.M12, matrix.M11);
				if (IsNotClose(matrix.M11, Math.Cos(ang)))
					return false;
				if (IsNotClose(matrix.M21, -Math.Sin(ang)))
                    return false;
                if (IsNotClose(matrix.M12, Math.Sin(ang)))
                    return false;
                angleRadians = ang;
                return true;
			}

			private static bool IsNotClose(double m33, double v)
			{
                return Math.Abs(m33 - v) > 1.0e-10;
			}

			private List<XbimPlacementNode>? _children;
            private bool _isAdjustedToGlobal;

            /// <summary>
            /// Standard constructor
            /// </summary>
            /// <param name="placement"></param>
            /// <param name="logger"></param>
            /// <param name="engine">required for some of the <see cref="IIfcObjectPlacement"/> types</param>
            public XbimPlacementNode(IIfcObjectPlacement placement, ILogger? logger = null, IXbimGeometryEngine? engine = null)
            {
                PlacementLabel = placement.EntityLabel;
                if (placement is IIfcLocalPlacement interfaceLocalPlacement)
                {
                    Matrix = interfaceLocalPlacement.RelativePlacement.ToMatrix3D();
                }
                else if (engine is null)
				{
					logger?.LogError("XbimPlacementNode for entity #{label} of type {type} needs a non null engine parameter. An identity matrix was used instead, related objects might result misplaced.", placement.EntityLabel, placement.GetType().Name);
					Matrix = XbimMatrix3D.Identity;
				}
				else if (placement is Ifc4x3.GeometricConstraintResource.IfcLinearPlacement interfaceLinearPlacement)
                {
                    if (LinearPlacement.LinearPlacement.TryGetPlacement(interfaceLinearPlacement, out var t, logger))
                        Matrix = t;
                    else
                        Matrix = XbimMatrix3D.Identity;
                }
                else if (placement is IIfcGridPlacement interfaceGridPlacement)
				{
					Matrix = engine.ToMatrix3D(interfaceGridPlacement, logger);
				}
				else
                {
                    logger?.LogError("XbimPlacementNode for entity #{label} of type {type} is not implemented. An identity matrix was used instead, related objects might result misplaced.", placement.EntityLabel, placement.GetType().Name);
                    Matrix = XbimMatrix3D.Identity;
                }
                _isAdjustedToGlobal = false;
            }

            public int PlacementLabel { get; private set; }
            public XbimMatrix3D Matrix { get; protected internal set; }

            public List<XbimPlacementNode> Children => _children ??= new List<XbimPlacementNode>();

            public XbimPlacementNode? Parent { get; set; } = null;

            internal void ToGlobalMatrix()
            {
                if (!_isAdjustedToGlobal && Parent != null)
                {
                    Parent.ToGlobalMatrix();
                    Matrix *= Parent.Matrix;
                }
                _isAdjustedToGlobal = true;
            }
        }
    }
}
#nullable restore