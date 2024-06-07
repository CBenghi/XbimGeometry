using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Isam.Esent.Interop;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using System.Transactions;
using Xbim.Common.Configuration;
using Xbim.Common.Geometry;
using Xbim.Common.XbimExtensions;
using Xbim.Geometry.Engine.Interop;
using Xbim.Ifc;
using Xbim.Ifc4.GeometricConstraintResource;
using Xbim.Ifc4.GeometryResource;
using Xbim.Ifc4.Interfaces;
using Xbim.IO.Memory;
using Xbim.ModelGeometry.Scene;
using Xunit;
using Xunit.Abstractions;

namespace Xbim.Geometry.Engine.Tests
{

	public class LocationAndPlacementTests
	{
		private readonly ILogger _logger;
		private readonly ILoggerFactory _loggerFactory;
		ITestOutputHelper _helper;

		public LocationAndPlacementTests(ILoggerFactory loggerFactory, ITestOutputHelper helper)
		{
			_helper = helper;
			_loggerFactory = loggerFactory;
			_logger = _loggerFactory.CreateLogger<LocationAndPlacementTests>();
		}

		[Fact]
		// [DeploymentItem("TestFiles\\LargeTriangulatedCoordinates.ifc")]
		public void LargeCoordinatesDisplacementTest()
		{
			using (var m = new MemoryModel(new Ifc2x3.EntityFactoryIfc2x3()))
			{
				m.LoadStep21("TestFiles\\LargeTriangulatedCoordinates.ifc");
				var c = new Xbim3DModelContext(m);
				c.CreateContext(null, false);

				using (var store = m.GeometryStore.BeginRead())
				{
					// region should be placed far away from origin
					var region = store.ContextRegions.FirstOrDefault().MostPopulated();
					var centre = region.Centre;
					centre.X.Should().BeGreaterThan(300000);
					centre.Y.Should().BeGreaterThan(6200000);

					var product = m.Instances.FirstOrDefault<IIfcBuildingElementProxy>();

					product.Representation.Should().NotBeNull();

					var instances = store.ShapeInstancesOfEntity(product);
					instances.Count().Should().Be(1);

					var instance = instances.First();
					var geometry = store.ShapeGeometryOfInstance(instance);

					// this should be a large displacement
					var transform = instance.Transformation;
					var length = transform.Translation.Length;
					length.Should().BeGreaterThan(6200000);

					// geometry thould be in small numbers
					var ms = new MemoryStream(((IXbimShapeGeometryData)geometry).ShapeData);
					var br = new BinaryReader(ms);
					var tr = br.ReadShapeTriangulation();

					var point = tr.Vertices.FirstOrDefault();
					point.X.Should().BeLessThan(1000);
					point.Y.Should().BeLessThan(1000);
					point.Z.Should().BeLessThan(1000);

					// when transformation is applied to the geometry it should be large
					tr = tr.Transform(transform);
					point = tr.Vertices.FirstOrDefault();
					point.X.Should().BeGreaterThan(300000);
					point.Y.Should().BeGreaterThan(6200000);
				}
			}
		}

		[Fact]
		public void MoveAndCopyTest()
		{
			//this test checks that a object is correctly copied and moved
			//create a box
			using (var m = new MemoryModel(new Xbim.Ifc4.EntityFactoryIfc4()))
			{
				using (var txn = m.BeginTransaction("Test"))
				{
					{
						var block = IfcModelBuilder.MakeBlock(m, 10, 10, 10);
						var geomEngine = new XbimGeometryEngine(m, _loggerFactory);
						var solid = geomEngine.CreateSolid(block, _logger);
						var ax3D = IfcModelBuilder.MakeAxis2Placement3D(m);
						ax3D.Location.Y = 100;
						var solidA = geomEngine.Moved(solid, ax3D) as IXbimSolid;
						solidA.Should().NotBeNull();
						Math.Abs(solidA.Volume - solid.Volume).Should().BeLessThan(1e-9, "Volume has changed");
						var displacement = solidA.BoundingBox.Centroid() - solid.BoundingBox.Centroid();
						displacement.Should().BeEquivalentTo(new XbimVector3D(0, 100, 0));
						var bbA = solidA.BoundingBox;
						var solidB = geomEngine.Moved(solid, ax3D);
						(bbA.Centroid() - solidB.BoundingBox.Centroid()).Should().BeEquivalentTo(new XbimVector3D(0, 0, 0));
					}
				}
			}
		}

		[Fact]
		public void ScaleAndCopyTest()
		{
			//this test checks that a object is correctly copied and moved
			//create a box
			using (var m = new MemoryModel(new Xbim.Ifc4.EntityFactoryIfc4()))
			{
				using (var txn = m.BeginTransaction("Test"))
				{
					var block = IfcModelBuilder.MakeBlock(m, 10, 10, 10);
					var geomEngine = new XbimGeometryEngine(m, _loggerFactory);
					var solid = geomEngine.CreateSolid(block, _logger);
					var transform = IfcModelBuilder.MakeCartesianTransformationOperator3D(m);
					transform.Scale = 2;
					var solidA = geomEngine.Transformed(solid, transform);
					var bb = solid.BoundingBox;
					var bbA = solidA.BoundingBox;
					Math.Abs(bb.Volume - 1000).Should().BeLessThan(1e-9, "Bounding box volume is incorrect in original shape");
					Math.Abs(bbA.Volume - 8000).Should().BeLessThan(1e-9, "Bounding box volume is incorrect in scaled shape");
					var transformNonUniform = IfcModelBuilder.MakeCartesianTransformationOperator3DnonUniform(m);
					transformNonUniform.Scale3 = 100;
					var solidB = geomEngine.Transformed(solid, transformNonUniform);
					Math.Abs(solidB.BoundingBox.Volume - 100000).Should().BeLessThan(1e-9, "Bounding box volume is incorrect in non uniform scaled shape");
					Math.Abs(bb.Volume - 1000).Should().BeLessThan(1e-9, "Bounding box volume of original shape as been changed");
				}
			}
		}

		[Fact]
		public void ObjectPlacementTest()
		{
			//this test checks that a object is correctly copied and moved
			//create a box
			using (var m = new MemoryModel(new Xbim.Ifc4.EntityFactoryIfc4()))
			{
				using (var txn = m.BeginTransaction("Test"))
				{
					var block = IfcModelBuilder.MakeBlock(m, 50, 10, 10);
					var geomEngine = new XbimGeometryEngine(m, _loggerFactory);
					var solid = geomEngine.CreateSolid(block, _logger);
					var placement = IfcModelBuilder.MakeLocalPlacement(m);
					((IfcAxis2Placement3D)placement.RelativePlacement).Location.X = 100;
					var bb = solid.BoundingBox;
					var solidA = geomEngine.Moved(solid, placement, _logger) as IXbimSolid;
					solidA.Should().NotBeNull();
					var displacement = solidA.BoundingBox.Centroid() - solid.BoundingBox.Centroid();
					displacement.Should().BeEquivalentTo(new XbimVector3D(100, 0, 0));

					var placementRelTo = ((IfcLocalPlacement)placement.PlacementRelTo);
					var zDir = m.Instances.New<IfcDirection>(d => d.SetXYZ(0, 0, 1));
					((IfcAxis2Placement3D)placementRelTo.RelativePlacement).Axis = zDir;
					var yDir = m.Instances.New<IfcDirection>(d => d.SetXYZ(0, 1, 0));
					((IfcAxis2Placement3D)placementRelTo.RelativePlacement).RefDirection = yDir; //point in Y
					((IfcAxis2Placement3D)placementRelTo.RelativePlacement).Location.X = 2000;
					var solidB = geomEngine.Moved(solid, placement, _logger) as IXbimSolid;
					displacement = solidB.BoundingBox.Centroid() - solid.BoundingBox.Centroid();
					var meshbuilder = new MeshHelper();
					geomEngine.Mesh(meshbuilder, solidB, m.ModelFactors.Precision, m.ModelFactors.DeflectionTolerance);
					var box = meshbuilder.BoundingBox;
					displacement.Should().BeEquivalentTo(new XbimVector3D(1970, 120, 0));


				}
			}
		}

		[Fact]
		public void GridPlacementTest()
		{
			//this test checks that a object is correctly copied and moved
			//create a box
			using (var m = new MemoryModel(new Xbim.Ifc4.EntityFactoryIfc4()))
			{
				using (var txn = m.BeginTransaction("Test"))
				{
					var geomEngine = new XbimGeometryEngine(m, _loggerFactory);
					var block = IfcModelBuilder.MakeBlock(m, 10, 10, 10);
					var solid = geomEngine.CreateSolid(block, _logger);
					var grid = IfcModelBuilder.MakeGrid(m, 3, 100);
					var gridPlacement = m.Instances.New<IfcGridPlacement>();
					gridPlacement.PlacementLocation = m.Instances.New<IfcVirtualGridIntersection>();
					gridPlacement.PlacementLocation.IntersectingAxes.Add(grid.UAxes.Last());
					gridPlacement.PlacementLocation.IntersectingAxes.Add(grid.VAxes.Last());
					var solidA = geomEngine.Moved(solid, gridPlacement, _logger) as IXbimSolid;
					solidA.Should().NotBeNull();
					var displacement = solidA.BoundingBox.Centroid() - solid.BoundingBox.Centroid();
					displacement.Should().BeEquivalentTo(new XbimVector3D(200, 200, 0));
				}
			}
		}
		[Theory]
		[InlineData(1, 0, 0, 1, 0)]
		[InlineData(1, 0, PiFourths, 0.7, 0.7)]
		[InlineData(1, 0, PiHalf, 0, 1)]
		public void ClothoidRotationMatrix(double x1, double y1, double ang1, double expX, double expY)
		{
			XbimPoint3D p = new XbimPoint3D(x1, y1, 0);
			var rot = ModelGeometry.Scene.LinearPlacement.ShapeClothoid.CreateRotationZ(ang1);
			var trsf = p * rot;
			trsf.X.Should().BeApproximately(expX, 0.05);
			trsf.Y.Should().BeApproximately(expY, 0.05);
		}

		const double PiFourths = 0.78539816339;
		const double PiHalf = 1.57079632679;

		[Theory]
		[InlineData(0, 0, 0, 1, 1, 0, 1, 1, 2, 1)]
		[InlineData(0, 0, PiFourths, 1, 1, 0, 1.41, 0, 2.12, -0.70)]
		[InlineData(1, 1, 0, 0, 0, 0, -1, -1, 0, -1)]
		[InlineData(1, 1, 0, 0, 0, PiFourths, -1, -1, -0.3, -0.3)]
		public void ClothoidTranslationTests(double x1, double y1, double ang1, double x2, double y2, double ang2,
			double expOrigX, double expOrigY,
			double expXunitX, double expXunitY
			)
		{
			var t = ModelGeometry.Scene.LinearPlacement.ShapeClothoid.GetTransform(x1, y1, ang1, x2, y2, ang2);
			var vtrTrsf = new XbimVector3D(1, 0, 0);
			// a point at origin means a point of on x2,y2
			XbimPoint3D pOrig = new XbimPoint3D(0, 0, 0);
			var relOrig = pOrig * t;
			XbimPoint3D pXunit = new XbimPoint3D(1, 0, 0);
			var relXUnit = pXunit * t;
			_logger.LogInformation("porig x: {px} porig y {py}", relOrig.X, relOrig.Y);
			_logger.LogInformation("pxunit x: {px} pxunit y {py}", relXUnit.X, relXUnit.Y);
			_logger.LogInformation("{m11} {m21} {m31} {m41}", t.M11, t.M21, t.M31, t.OffsetX);
			_logger.LogInformation("{m12} {m22} {m32} {m42}", t.M12, t.M22, t.M32, t.OffsetY);
			_logger.LogInformation("{m13} {m23} {m33} {m43}", t.M13, t.M23, t.M33, t.OffsetZ);
			_logger.LogInformation("{m14} {m24} {m34} {m44}", t.M14, t.M24, t.M34, t.M44);

			if (!double.IsNaN(expOrigX))
				relOrig.X.Should().BeApproximately(expOrigX, 0.05);
			if (!double.IsNaN(expOrigY))
				relOrig.Y.Should().BeApproximately(expOrigY, 0.05);

			if (!double.IsNaN(expXunitX))
				relXUnit.X.Should().BeApproximately(expXunitX, 0.05);
			if (!double.IsNaN(expXunitY))
				relXUnit.Y.Should().BeApproximately(expXunitY, 0.05);

		}

		[Theory]
		[InlineData(14,0,0,0,0)]
		[InlineData(14,1, 0.9999995010869064, 0.0007270405456135122, 0.0020663265306122445)]
		[InlineData(14,-1,-0.99999950108690641, -0.0007270405456135122, -0.0020663265306122445)]
		[InlineData(-14,1, 0.9999995010869064, -0.0007270405456135122, -0.0020663265306122445)]
		[InlineData(-14,-1, -0.9999995010869064, 0.0007270405456135122, 0.0020663265306122445)]
		public void ClothoidMathTest(double constant, double s, double expX, double expY, double expAngle)
		{
			ModelGeometry.Scene.LinearPlacement.ShapeClothoid.GetCoreClothoid(constant, s, 0.1, out var x, out var y, out var ang);
			x.Should().Be(expX);
			y.Should().Be(expY);
			ang.Should().Be(expAngle);
		}

		[Fact]
		private void LinearPlacementTest()
		{
			using var m = new MemoryModel(new Xbim.Ifc4x3.EntityFactoryIfc4x3Add2());
			using var txn = m.BeginTransaction("Test");
			var geomEngine = new XbimGeometryEngine(m, _loggerFactory);

			var block = IfcModelBuilder4x3.MakeBlock(m, 10, 10, 10);
			var solid = geomEngine.CreateSolid(block, _logger);

			var linPlacement = m.Instances.New<Xbim.Ifc4x3.GeometricConstraintResource.IfcLinearPlacement>();
			linPlacement.PlacementRelTo = IfcModelBuilder4x3.MakeLocalPlacement(m);
			var relP = m.Instances.New<Ifc4x3.GeometryResource.IfcAxis2PlacementLinear>();
			linPlacement.RelativePlacement = relP;

			var rt = IfcModelBuilder4x3.MakePointGradientCurve(m);
			relP.Location = IfcModelBuilder4x3.MakePointDistanceExpression(m, 20);
		}
		
		[Theory]
		[InlineData("TestFiles\\Ifc4x3Models\\LinearPlacementOfSignal.ifc", 3021)]
		[InlineData("TestFiles\\Ifc4x3Models\\Armamento.ifc", -1)]
		public void LinearPlacementFileTest(string filename, int entityLabel)
		{
			using var m = IfcStore.Open(filename);
			var ge = new XbimGeometryEngine(m, XbimServices.Current.GetLoggerFactory());
			XbimPlacementTree tree = new XbimPlacementTree(m, true, GetXunitLogger(_helper), ge);
			if (entityLabel != -1)
				tree[entityLabel].Should().NotBe(XbimMatrix3D.Identity);
		}

		internal static ILogger GetXunitLogger(ITestOutputHelper helper)
		{
			var services = new ServiceCollection()
						.AddLogging((builder) => builder.AddXUnit(helper).SetMinimumLevel(LogLevel.Debug));
			IServiceProvider provider = services.BuildServiceProvider();
			var logg = provider.GetRequiredService<ILogger<LocationAndPlacementTests>>();
			Assert.NotNull(logg);
			return logg;
		}

		[Fact]
		public void TransformSolidRectangularProfileDef()
		{
			using (var m = new MemoryModel(new Xbim.Ifc4.EntityFactoryIfc4()))
			{
				using (var txn = m.BeginTransaction("Test"))
				{
					var profile = IfcModelBuilder.MakeRectangleHollowProfileDef(m, 20, 10, 1);
					var extrude = IfcModelBuilder.MakeExtrudedAreaSolid(m, profile, 40);
					var geomEngine = new XbimGeometryEngine(m, _loggerFactory);
					var solid = geomEngine.CreateSolid(extrude, _logger);
					var transform = new XbimMatrix3D(); //test first with identity
					var solid2 = (IXbimSolid)solid.Transform(transform);
					var s1Verts = solid.Vertices.ToList();
					var s2Verts = solid2.Vertices.ToList();
					for (int i = 0; i < s1Verts.Count; i++)
					{
						XbimVector3D v = s1Verts[i].VertexGeometry - s2Verts[i].VertexGeometry;
						v.Length.Should().BeLessThan(m.ModelFactors.Precision, "vertices not the same");
					}
					transform.RotateAroundXAxis(Math.PI / 2);
					transform.RotateAroundYAxis(Math.PI / 4);
					transform.RotateAroundZAxis(Math.PI);
					transform.OffsetX += 100;
					transform.OffsetY += 200;
					transform.OffsetZ += 300;
					solid2 = (IXbimSolid)solid.Transform(transform);
					Math.Abs(solid.Volume - solid2.Volume).Should().BeLessThan(0.001, "Volume differs");
					transform.Invert();
					solid2 = (IXbimSolid)solid2.Transform(transform);
					s1Verts = solid.Vertices.ToList();
					s2Verts = solid2.Vertices.ToList();
					for (int i = 0; i < s1Verts.Count; i++)
					{
						XbimVector3D v = s1Verts[i].VertexGeometry - s2Verts[i].VertexGeometry;
						v.Length.Should().BeLessThan(m.ModelFactors.Precision, "vertices not the same");
					}
					txn.Commit();
				}
			}
		}
	}
}
