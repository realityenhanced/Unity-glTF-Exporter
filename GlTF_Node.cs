﻿#if UNITY_EDITOR
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class GlTF_Node : GlTF_Writer {
    // https://github.com/KhronosGroup/glTF/tree/master/specification/2.0#reference-node
	public int cameraIndex = -1;
	public bool hasParent = false;
	public List<string> childrenNames = new List<string>();
    public bool uniqueItems = true;

    // https://github.com/UX3D-nopper/glTF/tree/master_lights_blinnphong/extensions/Khronos/KHR_lights
	public int lightIndex = -1;

    public List<string>bufferViewNames = new List<string>();
	public List<string>indexNames = new List<string>();
	public List<string>accessorNames = new List<string>();
	public int meshIndex = -1;
	public GlTF_Matrix matrix;
	//	public GlTF_Mesh mesh;
	public GlTF_Rotation rotation;
	public GlTF_Scale scale;
	public GlTF_Translation translation;
	public int skinIndex = -1;
	public List<string> skeletons = new List<string>();
	public bool additionalProperties = false;

	public static string GetNameFromObject(Object o)
	{
		return "node_" + GlTF_Writer.GetNameFromObject(o, true);
	}

	public override void Write ()
	{
		Indent();
		jsonWriter.Write ("{\n");
		IndentIn();
		Indent();
		CommaNL();
		jsonWriter.Write ("\"name\": \"" + id + "\"");
		if (cameraIndex != -1)
		{
			CommaNL();
			Indent();
			jsonWriter.Write ("\"camera\": " + cameraIndex);
		}
		else if (lightIndex != -1)
		{
			CommaNL();
			Indent();
            jsonWriter.Write("\"extensions\" : { \"KHR_lights\" : { ");
            jsonWriter.Write ("\"light\":" + lightIndex);
            jsonWriter.Write(" } }");
        }
		else if (meshIndex != -1)
		{
			CommaNL();
			Indent();
			jsonWriter.Write ("\"mesh\": " + meshIndex);
		}

		if (childrenNames != null && childrenNames.Count > 0)
		{
			CommaNL();
			Indent();	jsonWriter.Write ("\"children\": [\n");
			IndentIn();
			foreach (string ch in childrenNames)
			{
				CommaNL();
				Indent();		jsonWriter.Write (GlTF_Writer.nodeNames.IndexOf(ch));
			}
			jsonWriter.WriteLine();
			IndentOut();
			Indent();	jsonWriter.Write ("]");
		}

		if (matrix != null)
		{
			CommaNL();
			matrix.Write();
		}
		else
		{
			if (translation != null && (translation.items[0] != 0f || translation.items[1] != 0f || translation.items[2] != 0f))
			{
				CommaNL();
				translation.Write();
			}
			if (scale != null && (scale.items[0] != 1f || scale.items[1] != 1f || scale.items[2] != 1f))
			{
				CommaNL();
				scale.Write();
			}
			if (rotation != null && (rotation.items[0] != 0f || rotation.items[1] != 0f || rotation.items[2] != 0f || rotation.items[3] != 0f))
			{
				CommaNL();
				rotation.Write();
			}
		}
		jsonWriter.Write("\n");

		if (skinIndex > -1)
		{
			CommaNL();
			Indent(); jsonWriter.Write("\"skin\": " + skinIndex + "\n");
		}

		IndentOut();
		Indent();		jsonWriter.Write ("}");
	}
}
#endif