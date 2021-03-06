﻿#if UNITY_EDITOR
using UnityEngine;
using System.Collections;

public class GlTF_DirectionalLight : GlTF_Light {
    public GlTF_DirectionalLight() { type = "directional"; }
    public override void Write()
	{
        Indent(); jsonWriter.Write("{\n");
        WriteColorAndType();
        jsonWriter.Write("\n");
        Indent(); jsonWriter.Write("}");
    }
}
#endif