using glTF_BinExporter;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Newtonsoft.Json;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GhGltfConverter
{
    public class glTfGhConverterComponent : GH_Component
    {
        /// <summary>
        /// Initialize the Component display info
        /// </summary>
        public glTfGhConverterComponent()
          : base("glTF-GhConverter", "glTF", "Convert mesh to glTF", "Stratum", "")
        {
        }

        /// <summary>
        /// Register all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddMeshParameter("Meshes", "Mesh", "Meshes to be converted", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Material indices", "Idx", "Material index for each mesh", GH_ParamAccess.list, 0);
            pManager.AddTextParameter("Materials", "Mat", "Materials JSON", GH_ParamAccess.list, "");
            pManager.AddBooleanParameter("Draco", "Draco", "Draco compression", GH_ParamAccess.item, false);
        }

        /// <summary>
        /// Register all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("glTF", "T", "glTF", GH_ParamAccess.item);
        }

        /// <summary>
        /// Do the work.
        /// </summary>
        /// <param name="DA">The DA object can be used to retrieve data from input parameters and 
        /// to store data in output parameters.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // retrieve all data from the input parameters.
            List<Mesh> meshes = new List<Mesh>();
            List<int> materialIndices = new List<int>();  // a list, same size as meshes, with the material index for the corresponding mesh
            List<string> materialSpecs = new List<string>();
            bool doDraco = true;

            // When data cannot be extracted from a parameter, abort.
            if (!DA.GetDataList(0, meshes))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Unable to get Meshes input");
                return;
            }
            if (!DA.GetDataList(1, materialIndices))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Unable to get material indices");
                return;
            }
            if (!DA.GetDataList(2, materialSpecs))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Unable to get material specs");
                return;
            }
            if (!DA.GetData(3, ref doDraco))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Unable to get Draco input");
                return;
            }

            // GH doesn't allow and empty list as a default parameter, so change it back to empty here if the is only one empty string
            if (materialSpecs.Count == 1 && string.IsNullOrEmpty(materialSpecs[0])) materialSpecs.Clear();


            // the converter wants a Rhino doc
            RhinoDoc rhinoDoc = RhinoDoc.CreateHeadless(null);
            meshes.ForEach(m => rhinoDoc.Objects.AddMesh(m));

            glTFExportOptions opts = new glTFExportOptions();
            opts.UseDracoCompression = doDraco;
            opts.UseDisplayColorForUnsetMaterials = true;  // if "false" still works here, but fails in Rhino server

            glTFLoader.Schema.Gltf gltf = DoConversion(opts, rhinoDoc.Objects, rhinoDoc.RenderSettings.LinearWorkflow);
            rhinoDoc.Objects.Clear();
            rhinoDoc.Dispose();

            SetMaterials(gltf, materialIndices, materialSpecs);

            // assign the output parameter.
            DA.SetData(0, glTFLoader.Interface.SerializeModel(gltf));
        }

        private class InterimMaterialSpec
        {
            public float[] BaseColorFactor;
            public float MetallicFactor;
            public float RoughnessFactor;
            public string TextureFilename;
        };

        private void SetMaterials(glTFLoader.Schema.Gltf gltf, List<int> materialIndices, List<string> materialSpecs)
        {
            // Had hoped to pass in a JSON string to serialize into MaterialSpec's, but JSON currently has a version conflict in Rhino.
            // Maybe it goes away someday.
            // For now, the material specs are passed in as strings, each of which contains a precise number of substrings, separated by semi-colons.
            // The material spec string corresponds, in order, to:
            // 0,1,2) the RGB values for BaseColorFactor, 3) the alpha value for the color,
            // 4) MetallicFactor, 5) RoughnessFactor,
            // 6) the Texture filename (or empty string)
            // Thus the following super-kludge loop (this will be one line of code when JSON becomes available.)
            List<InterimMaterialSpec> interimMaterials = new List<InterimMaterialSpec>();
            for (int i = 0; i < materialSpecs.Count; i++)
            {
                string mString = materialSpecs[i];
                List<string> specs = mString.Split(';').ToList();
                List<float> floats = (from numStr in specs.Take(6) select (float)Convert.ToDouble(numStr)).ToList();  // the first 6 values are floats
                InterimMaterialSpec interimMaterial = new InterimMaterialSpec();
                interimMaterial.BaseColorFactor = new float[] { floats[0], floats[1], floats[2], floats[3] };
                interimMaterial.MetallicFactor = floats[4];
                interimMaterial.RoughnessFactor = floats[5];
                interimMaterial.TextureFilename = specs.Count > 6 ? specs[6] : "";  // a string
                interimMaterials.Add(interimMaterial);
            }


            // Use the interim material specs (simple types from GH) to create glTF objects
            List<glTFLoader.Schema.Material> gltfMaterials = new List<glTFLoader.Schema.Material>();
            List<glTFLoader.Schema.Texture> gltfTextures = new List<glTFLoader.Schema.Texture>();
            List<glTFLoader.Schema.Image> gltfImages = new List<glTFLoader.Schema.Image>();
            for (int i = 0; i < interimMaterials.Count; i++)
            {
                InterimMaterialSpec im = interimMaterials[i];
                glTFLoader.Schema.Material mat = new glTFLoader.Schema.Material();
                mat.PbrMetallicRoughness = new glTFLoader.Schema.MaterialPbrMetallicRoughness();
                if (string.IsNullOrEmpty(im.TextureFilename)) mat.PbrMetallicRoughness.BaseColorFactor = im.BaseColorFactor;
                mat.PbrMetallicRoughness.MetallicFactor = im.MetallicFactor;
                mat.PbrMetallicRoughness.RoughnessFactor = im.RoughnessFactor;

                if (!string.IsNullOrEmpty(im.TextureFilename))  // create the required texture objects to go along with this material
                {
                    // Texture object
                    glTFLoader.Schema.Texture texture = new glTFLoader.Schema.Texture();
                    texture.Source = i;  // this is the image # (created below)
                    texture.Sampler = 0;  // using the default Sampler provided by the converter
                    gltfTextures.Add(texture);

                    // Image object
                    glTFLoader.Schema.Image image = new glTFLoader.Schema.Image();
                    image.Uri = im.TextureFilename;
                    gltfImages.Add(image);

                    // set the BaseColorTexture of this Material point to the new Texture
                    glTFLoader.Schema.TextureInfo textureInfo = new glTFLoader.Schema.TextureInfo();
                    textureInfo.Index = i;  // the Texture just created, which points to the Image just created

                    mat.PbrMetallicRoughness.BaseColorTexture = textureInfo;
                }

                gltfMaterials.Add(mat);
            }

            if (gltfMaterials.Count > 0)
            {
                gltf.Materials = gltfMaterials.ToArray();
                if (gltfTextures.Count > 0) gltf.Textures = gltfTextures.ToArray();
                if (gltfImages.Count > 0) gltf.Images = gltfImages.ToArray();
                // need Samplers also, but using the default Sampler for now

                // Assign the appropriate Material to each Mesh
                // Using a kludge for now, knowing that 2/3's of the Meshes are Faces and the other 1/3 are Edges
                int numMeshes = gltf.Meshes.Length;
                int numFaces = numMeshes * 2 / 3;
                for (int i = 0; i < gltf.Meshes.Length; i++)
                {
                    glTFLoader.Schema.Mesh mesh = gltf.Meshes[i];
                    if (materialIndices.Count == 1)
                    {
                        mesh.Primitives[0].Material = materialIndices[0];
                    }
                    else
                    {
                        mesh.Primitives[0].Material = i < numFaces ? 0 : 1;
                    }
                }
            }

            // set the default (and only) sampler to do a mirrored repeat in both directions
            glTFLoader.Schema.Sampler sampler = gltf.Samplers[0];
            sampler.WrapS = glTFLoader.Schema.Sampler.WrapSEnum.MIRRORED_REPEAT;
            sampler.WrapT = glTFLoader.Schema.Sampler.WrapTEnum.MIRRORED_REPEAT;
        }

        public glTFLoader.Schema.Gltf DoConversion(glTFExportOptions options, IEnumerable<RhinoObject> rhinoObjects, Rhino.Render.LinearWorkflow workflow)
        {
            RhinoDocGltfConverter converter = new RhinoDocGltfConverter(options, false, rhinoObjects, workflow);
            return converter.ConvertToGltf();
        }

        /// <summary>
        /// The Exposure property controls where in the panel a component icon 
        /// will appear. There are seven possible locations (primary to septenary), 
        /// each of which can be combined with the GH_Exposure.obscure flag, which 
        /// ensures the component will only be visible on panel dropdowns.
        /// </summary>
        public override GH_Exposure Exposure => GH_Exposure.primary;

        /// <summary>
        /// Provides an Icon for every component that will be visible in the User Interface.
        /// Icons need to be 24x24 pixels.
        /// You can add image files to your project resources and access them like this:
        /// return Resources.IconForThisComponent;
        /// </summary>
        protected override System.Drawing.Bitmap Icon => null;

        /// <summary>
        /// Each component must have a unique Guid to identify it. 
        /// It is vital this Guid doesn't change otherwise old ghx files 
        /// that use the old ID will partially fail during loading.
        /// </summary>
        public override Guid ComponentGuid => new Guid("E505952C-0766-42C7-87EC-6F67BA20A3F8");
    }
}