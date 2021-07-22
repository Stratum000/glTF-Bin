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
            pManager.AddTextParameter("Materials", "Mat", "Materials JSON", GH_ParamAccess.list);
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
            public float[] EmissiveFactor;
            public float MetallicFactor;
            public float RoughnessFactor;
        };

        private void SetMaterials(glTFLoader.Schema.Gltf gltf, List<int> materialIndices, List<string> materialSpecs)
        {
            // Had hoped to pass in a JSON string to serialize into MaterialSpec's, but JSON currently has a version conflict in Rhino. Maybe it goes away someday
            // For now, the material specs are passed in as strings, each of which contains 5 numeric substrings.
            // The correspond, in order, to 1,2,3) the RGB values for EmissiveFactor, 4) MetallicFactor, 5) RoughnessFactor.
            // Thus the following super-kludge loop (this will be one line of code when JSON becomes available.)
            List<InterimMaterialSpec> interimMaterials = new List<InterimMaterialSpec>();
            foreach (string mString in materialSpecs)
            {
                List<string> specs = mString.Split(';').ToList();
                List<float> floats = (from numStr in specs select (float)Convert.ToDouble(numStr)).ToList();
                InterimMaterialSpec interimMaterial = new InterimMaterialSpec();
                interimMaterial.EmissiveFactor = new float[] { floats[0], floats[1], floats[2], 1f };
                interimMaterial.MetallicFactor = floats[3];
                interimMaterial.RoughnessFactor = floats[4];
                interimMaterials.Add(interimMaterial);
            }

            List<glTFLoader.Schema.Material> gltfMaterials = new List<glTFLoader.Schema.Material>();
            foreach (InterimMaterialSpec im in interimMaterials)
            {
                glTFLoader.Schema.Material mat = new glTFLoader.Schema.Material();
                //mat.EmissiveFactor = im.EmissiveFactor;
                mat.PbrMetallicRoughness = new glTFLoader.Schema.MaterialPbrMetallicRoughness();
                mat.PbrMetallicRoughness.MetallicFactor = im.MetallicFactor;
                mat.PbrMetallicRoughness.RoughnessFactor = im.RoughnessFactor;
                mat.PbrMetallicRoughness.BaseColorFactor = im.EmissiveFactor;

                gltfMaterials.Add(mat);
            }

            int idx = 0;
            int numMeshes = gltf.Meshes.Length;
            int numFaces = numMeshes * 2 / 3;
            foreach (glTFLoader.Schema.Mesh mesh in gltf.Meshes)
            {
                mesh.Primitives[0].Material = idx < numFaces ? 0 : 1;
                idx++;
            }

            gltf.Materials = gltfMaterials.ToArray();


        }

        public glTFLoader.Schema.Gltf DoConversion(glTFExportOptions options, IEnumerable<RhinoObject> rhinoObjects, Rhino.Render.LinearWorkflow workflow)
        {
            RhinoDocGltfConverter converter = new RhinoDocGltfConverter(options, false, rhinoObjects, workflow);
            return converter.ConvertToGltf();

            // temporary: modify the material of each object so it reflects a little
            //foreach (glTFLoader.Schema.Material m in gltf.Materials)
            //{
            //    m.EmissiveFactor = new float[] { .62F, .32F, .18F };
            //    m.PbrMetallicRoughness.MetallicFactor = .5F;
            //    m.PbrMetallicRoughness.RoughnessFactor = .5F;
            //}

            //JsonConvert.DeserializeObject

            //glTFLoader.Schema.Material mat1 = new glTFLoader.Schema.Material();
            //mat1.EmissiveFactor = new float[] { .99F, .05F, .05F };
            //mat1.PbrMetallicRoughness = new glTFLoader.Schema.MaterialPbrMetallicRoughness();
            //mat1.PbrMetallicRoughness.MetallicFactor = .5F;
            //mat1.PbrMetallicRoughness.RoughnessFactor = .5F;

            //glTFLoader.Schema.Material mat2 = new glTFLoader.Schema.Material();
            //mat2.EmissiveFactor = new float[] { .05F, .99F, .05F };
            //mat2.PbrMetallicRoughness = new glTFLoader.Schema.MaterialPbrMetallicRoughness();
            //mat2.PbrMetallicRoughness.MetallicFactor = .5F;
            //mat2.PbrMetallicRoughness.RoughnessFactor = .5F;

            //gltf.Materials = new glTFLoader.Schema.Material[] { mat1, mat2 };
            //int idx = 0;
            //int numMeshes = gltf.Meshes.Length;
            //int numFaces = numMeshes * 2 / 3;
            //foreach (glTFLoader.Schema.Mesh mesh in gltf.Meshes)
            //{
            //    mesh.Primitives[0].Material = idx < numFaces ? 0 : 1;
            //    idx++;
            //}

            //return glTFLoader.Interface.SerializeModel(gltf);



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