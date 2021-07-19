using glTF_BinExporter;
using Grasshopper;
using Grasshopper.Kernel;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

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
            pManager.AddMeshParameter("Meshes", "M", "Meshes to be converted", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Draco", "D", "Draco compression", GH_ParamAccess.item, false);
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
            bool doDraco = true;
            string glTFtext;

            // When data cannot be extracted from a parameter, abort.
            if (!DA.GetDataList(0, meshes))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Unable to get Meshes input");
                return;
            }
            if (!DA.GetData(1, ref doDraco))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Unable to get Draco input");
                return;
            }

            // the converter wants a Rhino doc
            RhinoDoc rhinoDoc = RhinoDoc.Create(null);
            meshes.ForEach(m => rhinoDoc.Objects.AddMesh(m));

            glTFExportOptions opts = new glTFExportOptions();
            opts.UseDracoCompression = doDraco;
            opts.UseDisplayColorForUnsetMaterials = true;  // if "false" still works here, but fails in Rhino server

            glTFtext = DoConversion(opts, rhinoDoc.Objects, rhinoDoc.RenderSettings.LinearWorkflow);

            rhinoDoc.Objects.Clear();
            rhinoDoc.Dispose();

            // assign the output parameter.
            DA.SetData(0, glTFtext);
        }

        public string DoConversion(glTFExportOptions options, IEnumerable<RhinoObject> rhinoObjects, Rhino.Render.LinearWorkflow workflow)
        {
            RhinoDocGltfConverter converter = new RhinoDocGltfConverter(options, false, rhinoObjects, workflow);
            glTFLoader.Schema.Gltf gltf = converter.ConvertToGltf();

            // temporary: modify the material of each object so it reflects a little
            foreach (glTFLoader.Schema.Material m in gltf.Materials)
            {
                m.EmissiveFactor = new float[] { .62F, .32F, .18F };
                m.PbrMetallicRoughness.MetallicFactor = .5F;
                m.PbrMetallicRoughness.RoughnessFactor = .5F;
            }
            return glTFLoader.Interface.SerializeModel(gltf);
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