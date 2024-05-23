using System;
using Xbim.Ifc4.Interfaces;

namespace Xbim.ModelGeometry.Scene.Extensions
{
    public static class IIfcRepresentationExtensions
    {

        /// <summary>
        /// returns true if the representation is a 3D Shape (solid or surface), if it is a curve or curve set returns false
        /// </summary>
        /// <param name="rep"></param>
        /// <returns>a boolean value, true if a body representation is found</returns>
        public static bool IsBodyRepresentation(this IIfcRepresentation rep)
        {
            if (string.IsNullOrEmpty(rep.RepresentationIdentifier)) 
                return false;
            string repIdentifier = rep.RepresentationIdentifier.Value;
            //if it is defined as body then it is candidate but exclude if it is using a line base representation
            switch (repIdentifier.ToLowerInvariant())
            {
                case "body":
                case "facetation":
                case "reference":
                    //this should always be defined in an ifc2x3 schema but if it is not assume a solid
                    if (!rep.RepresentationType.HasValue)
                        return true;
                    string repType = rep.RepresentationType.Value;
                    repType = repType.ToLowerInvariant();

					switch (repType)
                    {
                        case "solidmodel":
                        case "surfacemodel":
                        case "sweptsolid":
                        case "brep":
                        case "csg":
                        case "clipping":
                        case "advancedsweptsolid":
                        case "boundingbox":
                        case "sectionedspine":
                        case "mappedrepresentation":
                        case "tessellation":
                        case "advancedbrep":
                            return true; // we have a valid solid
                        case "geometricset":
                        case "geometriccurveset":
                        case "annotation2d":
                        case "curve2d":
                        case "curve3d":
                            return false; //ignore line based body representations
                        default:
                            return false;
                    }
                case "axis": 
                    return true; // we separate the explicily excluded identifiers for debug purposes
                case "footprint":
                    return false; // we separate the explicily excluded identifiers for debug purposes
				default:
                    return false;
            }
        }

    }
}

