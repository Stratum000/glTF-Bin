using Grasshopper;
using Grasshopper.Kernel;
using System;
using System.Drawing;

namespace GhGltfConverter
{
    public class glTfGhConverterInfo : GH_AssemblyInfo
    {
        public override string Name => "GhGltfConverter";

        //Return a 24x24 pixel bitmap to represent this GHA library.
        public override Bitmap Icon => null;

        //Return a short string describing the purpose of this GHA library.
        public override string Description => "";

        public override Guid Id => new Guid("E6577295-DFC8-40C1-9368-0D1A55898775");

        //Return a string identifying you or your company.
        public override string AuthorName => "";

        //Return a string representing your preferred contact details.
        public override string AuthorContact => "";
    }
}