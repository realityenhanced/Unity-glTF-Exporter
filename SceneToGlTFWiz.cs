/***************************************************************************
GlamExport
 - Unity3D Scriptable Wizard to export Hierarchy or Project objects as glTF


****************************************************************************/
using UnityEngine;
using UnityEditor;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Reflection;
using Ionic.Zip;

public enum IMAGETYPE
{
	GRAYSCALE,
	RGB,
	RGBA,
	NORMAL_MAP,
	IGNORE
}

public class SceneToGlTFWiz : MonoBehaviour
{
	public int jpgQuality = 92;

	public static Dictionary<string, string> UnityToPBRMetalChannel = new Dictionary<string, string>
	{
		{"_MainTex", "baseColorTexture" },
		{ "_MetallicGlossMap", "metallicTexture" },
		{"_BumpMap","normalTexture" },
		{"_OcclusionMap","aoTexture" },
		{"_EmissionMap", "emissiveTexture" },

		// Colors
		{"_Color","baseColorFactor" },
		{"_EmissionColor","emissiveFactor" },

		// Factors
		{"_Metallic", "metallicFactor" },
		{"_GlossMapScale", "roughnessFactor" },
		{"_Glossiness", "roughnessFactor" },
		{"_BumpScale", "normalFactor" },
		{"_OcclusionStrength", "aoFactor" }
	};

	public static Dictionary<string, string> UnityToPBRSpecularChannel = new Dictionary<string, string>
	{
		{"_MainTex", "diffuseTexture" },
		{"_SpecGlossMap", "specularTexture" },
		{"_BumpMap","normalTexture" },
		{"_OcclusionMap","aoTexture" },
		{"_EmissionMap", "emissiveTexture" },

		// Colors
		{"_Color","diffuseFactor" },
		{"_SpecColor", "specularFactor" },
		{"_EmissionColor","emissiveFactor" },

		// Factors
		{"_GlossMapScale", "glossinessFactor" },
		{"_Glossiness", "glossinessFactor" },
		{"_BumpScale", "normalFactor" },
		{"_OcclusionStrength", "aoFactor" }
	};

	public GlTF_Writer writer;
	string savedPath = "";
	bool exportPBRMaterials = true;
	string currentTransformName = "";
	int currentObjectIndex = 0;
	int nbSelectedObjects = 0;

	static bool done = true;
	bool parseLightmaps = false;

	public static void parseUnityCamera(Transform tr)
	{
		if (tr.GetComponent<Camera>().orthographic)
		{
			GlTF_Orthographic cam;
			cam = new GlTF_Orthographic();
			cam.type = "orthographic";
			cam.zfar = tr.GetComponent<Camera>().farClipPlane;
			cam.znear = tr.GetComponent<Camera>().nearClipPlane;
			cam.name = tr.name;
			//cam.orthographic.xmag = tr.camera.
			GlTF_Writer.cameras.Add(cam);
		}
		else
		{
			GlTF_Perspective cam;
			cam = new GlTF_Perspective();
			cam.type = "perspective";
			cam.zfar = tr.GetComponent<Camera>().farClipPlane;
			cam.znear = tr.GetComponent<Camera>().nearClipPlane;
			cam.aspect_ratio = tr.GetComponent<Camera>().aspect;
			cam.yfov = tr.GetComponent<Camera>().fieldOfView;
			cam.name = tr.name;
			GlTF_Writer.cameras.Add(cam);
		}
	}

	public bool isDone()
	{
		return done;
	}

	public void resetParser()
	{
		done = false;
	}

	public static void parseUnityLight(Transform tr)
	{
		switch (tr.GetComponent<Light>().type)
		{
			case LightType.Point:
				GlTF_PointLight pl = new GlTF_PointLight();
				pl.color = new GlTF_ColorRGB(tr.GetComponent<Light>().color);
				pl.name = tr.name;
				GlTF_Writer.lights.Add(pl);
				break;

			case LightType.Spot:
				GlTF_SpotLight sl = new GlTF_SpotLight();
				sl.color = new GlTF_ColorRGB(tr.GetComponent<Light>().color);
				sl.name = tr.name;
				GlTF_Writer.lights.Add(sl);
				break;

			case LightType.Directional:
				GlTF_DirectionalLight dl = new GlTF_DirectionalLight();
				dl.color = new GlTF_ColorRGB(tr.GetComponent<Light>().color);
				dl.name = tr.name;
				GlTF_Writer.lights.Add(dl);
				break;

			case LightType.Area:
				GlTF_AmbientLight al = new GlTF_AmbientLight();
				al.color = new GlTF_ColorRGB(tr.GetComponent<Light>().color);
				al.name = tr.name;
				GlTF_Writer.lights.Add(al);
				break;
		}
	}

	public void ExportCoroutine(string path, Preset presetAsset, bool buildZip, bool exportPBRMaterials, bool exportAnimation = true, bool doConvertImages = false)
	{
		StartCoroutine(Export(path, presetAsset, buildZip, exportPBRMaterials, exportAnimation, doConvertImages));
	}

	public int getCurrentIndex()
	{
		return currentObjectIndex;
	}

	public string getCurrentObjectName()
	{
		return currentTransformName;
	}

	public int getNbSelectedObjects()
	{
		return nbSelectedObjects;
	}

	public IEnumerator Export(string path, Preset presetAsset, bool buildZip, bool exportPBRMaterials, bool exportAnimation = true, bool doConvertImages = false)
	{
		writer = new GlTF_Writer();
		writer.Init ();
		done = false;
		bool debugRightHandedScale = false;
		GlTF_Writer.exportedFiles.Clear();
		if (debugRightHandedScale)
			GlTF_Writer.convertRightHanded = false;

		writer.extraString.Add("exporterVersion", GlTF_Writer.exporterVersion );

		// Create rootNode
		GlTF_Node correctionNode = new GlTF_Node();
		correctionNode.id = "UnityGlTF_correctionMatrix";
		correctionNode.name = "UnityGlTF_correctionMatrix";

		// Add correction matrix to reorient scene for left to right-handed coordinate systems
		Quaternion correctionQuat = Quaternion.Euler(0, 180, 0);
		writer.convertQuatLeftToRightHandedness(ref correctionQuat);
		Matrix4x4 correctionMat = Matrix4x4.TRS(Vector3.zero, correctionQuat, Vector3.one);
		GlTF_Writer.sceneRootMatrix = correctionMat;
		correctionNode.matrix = new GlTF_Matrix(correctionMat, false);
		GlTF_Writer.nodes.Add(correctionNode);
		GlTF_Writer.nodeNames.Add(correctionNode.name);
		GlTF_Writer.rootNodes.Add(correctionNode);


		// Check if scene has lightmap data
		bool hasLightmap = LightmapSettings.lightmaps.Length != 0;

		//path = toGlTFname(path);
		savedPath = Path.GetDirectoryName(path);

		// Temp list to keep track of skeletons
		Dictionary<string, GlTF_Skin> parsedSkins = new Dictionary<string, GlTF_Skin>();
		parsedSkins.Clear();

		// first, collect objects in the scene, add to lists
		Transform[] transforms = Selection.GetTransforms (SelectionMode.Deep);
		List<Transform> trs = new List<Transform>(transforms);
		// Prefilter selected nodes and look for skinning in order to list "bones" nodes
		//FIXME: improve this
		List<Transform> bones = new List<Transform>();
		foreach(Transform tr in trs)
		{
			if (!tr.gameObject.activeSelf)
				continue;

			SkinnedMeshRenderer skin = tr.GetComponent<SkinnedMeshRenderer>();
			if (skin)
			{
				foreach(Transform bone in skin.bones)
				{
					bones.Add(bone);
				}
			}
		}

		currentObjectIndex = 0;
		nbSelectedObjects = trs.Count;
		int nbDisabledObjects = 0;
		foreach (Transform tr in trs)
		{
			if (tr.gameObject.activeInHierarchy == false)
			{
				nbDisabledObjects++;
				continue;
			}

			currentTransformName = tr.name;
			currentObjectIndex++;

			// Initialize the node
			GlTF_Node node = new GlTF_Node();
			node.id = GlTF_Node.GetNameFromObject(tr);
			node.name = tr.name;

			if (tr.GetComponent<Camera>() != null)
				parseUnityCamera(tr);

			if (tr.GetComponent<Light>() != null)
				parseUnityLight(tr);

			Mesh m = GetMesh(tr);
			if (m != null)
			{
				GlTF_Mesh mesh = new GlTF_Mesh();
				mesh.name = GlTF_Mesh.GetNameFromObject(m) + tr.name;

				GlTF_Accessor positionAccessor = new GlTF_Accessor(GlTF_Accessor.GetNameFromObject(m, "position"), GlTF_Accessor.Type.VEC3, GlTF_Accessor.ComponentType.FLOAT);
				positionAccessor.bufferView = GlTF_Writer.vec3BufferView;
				GlTF_Writer.accessors.Add (positionAccessor);

				GlTF_Accessor normalAccessor = null;
				if (m.normals.Length > 0)
				{
					normalAccessor = new GlTF_Accessor(GlTF_Accessor.GetNameFromObject(m, "normal"), GlTF_Accessor.Type.VEC3, GlTF_Accessor.ComponentType.FLOAT);
					normalAccessor.bufferView = GlTF_Writer.vec3BufferView;
					GlTF_Writer.accessors.Add (normalAccessor);
				}

				GlTF_Accessor colorAccessor = null;
				if (m.colors.Length > 0)
				{
					colorAccessor = new GlTF_Accessor(GlTF_Accessor.GetNameFromObject(m, "color"), GlTF_Accessor.Type.VEC4, GlTF_Accessor.ComponentType.FLOAT);
					colorAccessor.bufferView = GlTF_Writer.vec4BufferView;
					GlTF_Writer.accessors.Add(colorAccessor);
				}

				GlTF_Accessor uv0Accessor = null;
				if (m.uv.Length > 0) {
					uv0Accessor =  new GlTF_Accessor(GlTF_Accessor.GetNameFromObject(m, "uv0"), GlTF_Accessor.Type.VEC2, GlTF_Accessor.ComponentType.FLOAT);
					uv0Accessor.bufferView = GlTF_Writer.vec2BufferView;
					GlTF_Writer.accessors.Add (uv0Accessor);
				}

				GlTF_Accessor uv1Accessor = null;
				if (m.uv2.Length > 0) {
					// check if object is affected by a lightmap
					uv1Accessor =  new GlTF_Accessor(GlTF_Accessor.GetNameFromObject(m, "uv1"), GlTF_Accessor.Type.VEC2, GlTF_Accessor.ComponentType.FLOAT);
					uv1Accessor.bufferView = GlTF_Writer.vec2BufferView;
					GlTF_Writer.accessors.Add (uv1Accessor);
				}

				GlTF_Accessor uv2Accessor = null;
				if (m.uv3.Length > 0) {
					uv2Accessor =  new GlTF_Accessor(GlTF_Accessor.GetNameFromObject(m, "uv2"), GlTF_Accessor.Type.VEC2, GlTF_Accessor.ComponentType.FLOAT);
					uv2Accessor.bufferView = GlTF_Writer.vec2BufferView;
					GlTF_Writer.accessors.Add (uv2Accessor);
				}

				GlTF_Accessor uv3Accessor = null;
				if (m.uv4.Length > 0) {
					uv3Accessor =  new GlTF_Accessor(GlTF_Accessor.GetNameFromObject(m, "uv3"), GlTF_Accessor.Type.VEC2, GlTF_Accessor.ComponentType.FLOAT);
					uv3Accessor.bufferView = GlTF_Writer.vec2BufferView;
					GlTF_Writer.accessors.Add (uv3Accessor);
				}

				GlTF_Accessor jointAccessor = null;
				if (exportAnimation && m.boneWeights.Length > 0)
				{
					jointAccessor = new GlTF_Accessor(GlTF_Accessor.GetNameFromObject(m, "joints"), GlTF_Accessor.Type.VEC4, GlTF_Accessor.ComponentType.FLOAT);
					jointAccessor.bufferView = GlTF_Writer.vec4BufferView;
					GlTF_Writer.accessors.Add(jointAccessor);
				}

				GlTF_Accessor weightAccessor = null;
				if (exportAnimation && m.boneWeights.Length > 0)
				{
					weightAccessor = new GlTF_Accessor(GlTF_Accessor.GetNameFromObject(m, "weights"), GlTF_Accessor.Type.VEC4, GlTF_Accessor.ComponentType.FLOAT);
					weightAccessor.bufferView = GlTF_Writer.vec4BufferView;
					GlTF_Writer.accessors.Add(weightAccessor);
				}

				GlTF_Accessor tangentAccessor = null;
				if (m.tangents.Length > 0)
				{
					tangentAccessor = new GlTF_Accessor(GlTF_Accessor.GetNameFromObject(m, "tangents"), GlTF_Accessor.Type.VEC4, GlTF_Accessor.ComponentType.FLOAT);
					tangentAccessor.bufferView = GlTF_Writer.vec4BufferView;
					GlTF_Writer.accessors.Add(tangentAccessor);
				}

				var smCount = m.subMeshCount;
				for (var i = 0; i < smCount; ++i)
				{
					GlTF_Primitive primitive = new GlTF_Primitive();
					primitive.name = GlTF_Primitive.GetNameFromObject(m, i);
					primitive.index = i;
					GlTF_Attributes attributes = new GlTF_Attributes();
					attributes.positionAccessor = positionAccessor;
					attributes.normalAccessor = normalAccessor;
					attributes.colorAccessor = colorAccessor;
					attributes.texCoord0Accessor = uv0Accessor;
					attributes.texCoord1Accessor = uv1Accessor;
					attributes.texCoord2Accessor = uv2Accessor;
					attributes.texCoord3Accessor = uv3Accessor;
					attributes.jointAccessor = jointAccessor;
					attributes.weightAccessor = weightAccessor;
					attributes.tangentAccessor = tangentAccessor;
					primitive.attributes = attributes;
					GlTF_Accessor indexAccessor = new GlTF_Accessor(GlTF_Accessor.GetNameFromObject(m, "indices_" + i), GlTF_Accessor.Type.SCALAR, GlTF_Accessor.ComponentType.USHORT);
					indexAccessor.bufferView = GlTF_Writer.ushortBufferView;
					GlTF_Writer.accessors.Add (indexAccessor);
					primitive.indices = indexAccessor;

					var mr = GetRenderer(tr);
					var sm = mr.sharedMaterials;
					if (i < sm.Length) {
						var mat = sm[i];
						var matName = GlTF_Material.GetNameFromObject(mat);
						if(GlTF_Writer.materialNames.Contains(matName))
						{
							primitive.materialIndex = GlTF_Writer.materialNames.IndexOf(matName); // THIS INDIRECTION CAN BE REMOVED!
						}
						else
						{
							GlTF_Material material = new GlTF_Material();
							material.name = mat.name;
							material.diffuse = new GlTF_MaterialColor ("diffuse", mat.color);
							GlTF_Writer.materialNames.Add(matName);
							GlTF_Writer.materials.Add (material);

							//technique
							var s = mat.shader;
							var techName = GlTF_Technique.GetNameFromObject(s);
							if(GlTF_Writer.techniqueNames.Contains(techName))
							{
								material.instanceTechniqueIndex = GlTF_Writer.techniqueNames.IndexOf(techName);// THIS INDIRECTION CAN BE REMOVED!
							}
							else
							{
								GlTF_Technique tech = new GlTF_Technique();
								tech.name = techName;
								GlTF_Technique.Parameter tParam = new GlTF_Technique.Parameter();
								tParam.name = "position";
								tParam.type = GlTF_Technique.Type.FLOAT_VEC3;
								tParam.semantic = GlTF_Technique.Semantic.POSITION;
								tech.parameters.Add(tParam);
								GlTF_Technique.Attribute tAttr = new GlTF_Technique.Attribute();
								tAttr.name = "a_position";
								tAttr.param = tParam.name;
								tech.attributes.Add(tAttr);

								if (normalAccessor != null)
								{
									tParam = new GlTF_Technique.Parameter();
									tParam.name = "normal";
									tParam.type = GlTF_Technique.Type.FLOAT_VEC3;
									tParam.semantic = GlTF_Technique.Semantic.NORMAL;
									tech.parameters.Add(tParam);
									tAttr = new GlTF_Technique.Attribute();
									tAttr.name = "a_normal";
									tAttr.param = tParam.name;
									tech.attributes.Add(tAttr);
								}

								if (uv0Accessor != null)
								{
									tParam = new GlTF_Technique.Parameter();
									tParam.name = "texcoord0";
									tParam.type = GlTF_Technique.Type.FLOAT_VEC2;
									tParam.semantic = GlTF_Technique.Semantic.TEXCOORD_0;
									tech.parameters.Add(tParam);
									tAttr = new GlTF_Technique.Attribute();
									tAttr.name = "a_texcoord0";
									tAttr.param = tParam.name;
									tech.attributes.Add(tAttr);
								}

								if (uv1Accessor != null)
								{
									tParam = new GlTF_Technique.Parameter();
									tParam.name = "texcoord1";
									tParam.type = GlTF_Technique.Type.FLOAT_VEC2;
									tParam.semantic = GlTF_Technique.Semantic.TEXCOORD_1;
									tech.parameters.Add(tParam);
									tAttr = new GlTF_Technique.Attribute();
									tAttr.name = "a_texcoord1";
									tAttr.param = tParam.name;
									tech.attributes.Add(tAttr);
								}

								if (uv2Accessor != null)
								{
									tParam = new GlTF_Technique.Parameter();
									tParam.name = "texcoord2";
									tParam.type = GlTF_Technique.Type.FLOAT_VEC2;
									tParam.semantic = GlTF_Technique.Semantic.TEXCOORD_2;
									tech.parameters.Add(tParam);
									tAttr = new GlTF_Technique.Attribute();
									tAttr.name = "a_texcoord2";
									tAttr.param = tParam.name;
									tech.attributes.Add(tAttr);
								}

								if (uv3Accessor != null)
								{
									tParam = new GlTF_Technique.Parameter();
									tParam.name = "texcoord3";
									tParam.type = GlTF_Technique.Type.FLOAT_VEC2;
									tParam.semantic = GlTF_Technique.Semantic.TEXCOORD_3;
									tech.parameters.Add(tParam);
									tAttr = new GlTF_Technique.Attribute();
									tAttr.name = "a_texcoord3";
									tAttr.param = tParam.name;
									tech.attributes.Add(tAttr);
								}

								tech.AddDefaultUniforms();

								// Populate technique with shader data
								GlTF_Writer.techniqueNames.Add (techName);
								GlTF_Writer.techniques.Add (tech);
								populateTechniqueFromShader(s, tech);

								// create program
								GlTF_Program program = new GlTF_Program();
								program.name = GlTF_Program.GetNameFromObject(s);
								tech.program = program.name;
								foreach (var attr in tech.attributes)
								{
									program.attributes.Add(attr.name);
								}
								GlTF_Writer.programs.Add(program);

								// Shaders are not written for now since PBR extension is used
								// shader
								//GlTF_Shader vs = new GlTF_Shader();
								//vs.name = GlTF_Shader.GetNameFromObject(s, GlTF_Shader.Type.Vertex);
								//program.vertexShader = vs.name;
								//vs.type = GlTF_Shader.Type.Vertex;
								//vs.uri = preset.GetVertexShader(s.name);
								//GlTF_Writer.shaders.Add(vs);

								//GlTF_Shader fs = new GlTF_Shader();
								//fs.name = GlTF_Shader.GetNameFromObject(s, GlTF_Shader.Type.Fragment);
								//program.fragmentShader = fs.name;
								//fs.type = GlTF_Shader.Type.Fragment;
								//fs.uri = preset.GetFragmentShader(s.name);
								//GlTF_Writer.shaders.Add(fs);

							}

							unityToPBRMaterial(mat, ref material, doConvertImages);
							// Handle lightmap
							if(parseLightmaps && hasLightmap)
							{
								KeyValuePair<GlTF_Texture,GlTF_Image> lightmapdata = exportLightmap(tr, ref primitive, ref material);
								if(lightmapdata.Key != null)
								{
									GlTF_Writer.textureNames.Add(lightmapdata.Key.name);
									GlTF_Writer.textures.Add(lightmapdata.Key);
									GlTF_Writer.images.Add(lightmapdata.Value);
								}
							}
						}
					}
					mesh.primitives.Add(primitive);
				}

				SkinnedMeshRenderer skin = tr.GetComponent<SkinnedMeshRenderer>();
				Mesh baked = m;
				// If skinned, bake mesh in order to end with good transforms
				// (Unity skinning directly uses mesh to deform it, and doesn't care about transform anymore)
				// Baking allow to take the current transform into account.
				// FIXME: could also avoid baking and use the mesh directly and reset the transform
				if (exportAnimation && skin)
				{
					baked = new Mesh();
					skin.BakeMesh(baked);
					baked.uv = m.uv;
					baked.uv2 = m.uv2;
					baked.uv3 = m.uv3;
					baked.uv4 = m.uv4;

					baked.bindposes = m.bindposes;
					baked.boneWeights = m.boneWeights;

					Matrix4x4 correction = Matrix4x4.TRS(tr.localPosition, tr.localRotation, tr.lossyScale).inverse * Matrix4x4.TRS(tr.localPosition, tr.localRotation, Vector3.one);
					if(!correction.isIdentity)
					{
						Vector3[] verts = baked.vertices;
						Vector3[] norms = baked.normals;
						Vector4[] tangents = baked.tangents;
						for (int i = 0; i < verts.Length; ++i)
						{
							verts[i] = correction.MultiplyPoint3x4(verts[i]);
							norms[i] = correction.MultiplyVector(norms[i]);
							norms[i].Normalize();
						}
						baked.vertices = verts;
						baked.normals = norms;
						baked.RecalculateBounds();
					}
				}

				mesh.Populate(baked);
				GlTF_Writer.meshes.Add(mesh);
				node.meshIndexes.Add(GlTF_Writer.meshes.IndexOf(mesh));
			}

			// Parse animations
			if (exportAnimation)
			{
				Animator a = tr.GetComponent<Animator>();
				if (a != null)
				{
					AnimationClip[] clips = AnimationUtility.GetAnimationClips(tr.gameObject);
					for (int i = 0; i < clips.Length; i++)
					{
						//FIXME It seems not good to generate one animation per animator.
						GlTF_Animation anim = new GlTF_Animation(a.name, node.name);
						anim.Populate(clips[i], tr, GlTF_Writer.bakeAnimation);
						if(anim.channels.Count > 0)
							GlTF_Writer.animations.Add(anim);
					}
				}

				Animation animation = tr.GetComponent<Animation>();
				if (animation != null)
				{
					AnimationClip clip = animation.clip;
					//FIXME It seems not good to generate one animation per animator.
					GlTF_Animation anim = new GlTF_Animation(animation.name, node.name);
					anim.Populate(clip, tr, GlTF_Writer.bakeAnimation);
					if (anim.channels.Count > 0)
						GlTF_Writer.animations.Add(anim);
				}
			}

			// Parse transform
			if (tr.parent == null)
			{
				Matrix4x4 mat = Matrix4x4.identity;
				if(debugRightHandedScale)
					mat.m22 = -1;
				mat = mat * Matrix4x4.TRS(tr.localPosition, tr.localRotation, tr.localScale);
				node.matrix = new GlTF_Matrix(mat);
			}
			// Use good transform if parent object is not in selection
			else if (!trs.Contains(tr.parent))
			{
				node.hasParent = false;
				Matrix4x4 mat = Matrix4x4.identity;
				if(debugRightHandedScale)
					mat.m22 = -1;
				mat = mat * tr.localToWorldMatrix;
				node.matrix = new GlTF_Matrix(mat);
			}
			else
			{
				node.hasParent = true;
				if (tr.localPosition != Vector3.zero)
					node.translation = new GlTF_Translation (tr.localPosition);
				if (tr.localScale != Vector3.one)
					node.scale = new GlTF_Scale (tr.localScale);
				if (tr.localRotation != Quaternion.identity)
					node.rotation = new GlTF_Rotation (tr.localRotation);
			}

			if(!node.hasParent)
				correctionNode.childrenNames.Add(node.id);

			if (tr.GetComponent<Camera>() != null)
			{
				node.cameraName = tr.name;
			}
			else if (tr.GetComponent<Light>() != null)
				node.lightName = tr.name;

			// Parse node's skin data
			GlTF_Accessor invBindMatrixAccessor = null;
			SkinnedMeshRenderer skinMesh = tr.GetComponent<SkinnedMeshRenderer>();
			if (exportAnimation && skinMesh != null && skinMesh.enabled && checkSkinValidity(skinMesh, trs) && skinMesh.rootBone != null)
			{
				node.skeletons = GlTF_Skin.findRootSkeletons(skinMesh);
				GlTF_Skin skin = new GlTF_Skin();
				skin.setBindShapeMatrix(tr);
				skin.name = skinMesh.rootBone.name + "_skeleton_" + node.name + tr.GetInstanceID();

				// Create invBindMatrices accessor
				invBindMatrixAccessor = new GlTF_Accessor(skin.name + "invBindMatrices", GlTF_Accessor.Type.MAT4, GlTF_Accessor.ComponentType.FLOAT);
				invBindMatrixAccessor.bufferView = GlTF_Writer.mat4BufferView;
				GlTF_Writer.accessors.Add(invBindMatrixAccessor);

				// Generate skin data
				skin.Populate(tr, ref invBindMatrixAccessor, GlTF_Writer.accessors.Count -1);
				GlTF_Writer.skins.Add(skin);
				node.skinIndex = GlTF_Writer.skins.IndexOf(skin);
			}

			// The node is a bone?
			if (exportAnimation && bones.Contains(tr))
				node.jointName = GlTF_Node.GetNameFromObject(tr);

			foreach (Transform t in tr.transform)
			{
				if(t.gameObject.activeInHierarchy)
					node.childrenNames.Add(GlTF_Node.GetNameFromObject(t));
			}

			GlTF_Writer.nodeNames.Add(node.id);
			GlTF_Writer.nodes.Add (node);
		}

		// Other texture conversion method
		//if (doConvertImages)
		//    convertImages(ref GlTF_Writer.images, ref GlTF_Writer.exportedFiles, savedPath);

		writer.OpenFiles(path);
		writer.Write ();
		writer.CloseFiles();
		if(nbDisabledObjects > 0)
			Debug.Log(nbDisabledObjects + " disabled object ignored during export");

		if(GlTF_Writer.meshes.Count == 0)
		{
			Debug.Log("No visible objects have been exported. Aboring export");
				yield return false;
		}

		Debug.Log("Scene has been exported to " + path);
		if(buildZip)
		{
			ZipFile zip = new ZipFile();
			Debug.Log(GlTF_Writer.exportedFiles.Count + " files generated");
			string zipName = Path.GetFileNameWithoutExtension(path) + ".zip";

			zip.AddFiles(GlTF_Writer.exportedFiles, "");
			zip.Save(savedPath + "/" + zipName);

			// Remove all files
			foreach (string pa in GlTF_Writer.exportedFiles)
			{
				if (System.IO.File.Exists(pa))
					System.IO.File.Delete(pa);
			}

			Debug.Log("Files have been cleaned");
		}
		done = true;

		yield return true;
	}

	// Check if all the bones referenced by the skin are in the selection
	public bool checkSkinValidity(SkinnedMeshRenderer skin, List<Transform> selection)
	{
		string unselected = "";
		foreach(Transform t in skin.bones)
		{
			if (!selection.Contains(t))
			{
				unselected = unselected + "\n" + t.name;
			}
		}

		if(unselected.Length > 0)
		{
			Debug.LogError("Error while exportin skin for " + skin.name + " (skipping skinning export).\nClick for more details:\n \nThe following bones are used but are not selected" + unselected + "\n");
			return false;
		}

		return true;
	}

	public KeyValuePair<GlTF_Texture, GlTF_Image> exportLightmap(Transform tr, ref GlTF_Primitive primitive, ref GlTF_Material material)
	{
		MeshRenderer meshRenderer = tr.GetComponent<MeshRenderer>();
		KeyValuePair<GlTF_Texture, GlTF_Image> lightmapKV = new KeyValuePair<GlTF_Texture, GlTF_Image>();
		if(!meshRenderer || meshRenderer.lightmapIndex == -1)
		{
			Debug.Log("[ExportLightmap] No mesh renderer, return");
			return lightmapKV;
		}

		//FIXME what if object has no lightmap ?
		LightmapData lightmap = LightmapSettings.lightmaps[meshRenderer.lightmapIndex];
		Texture2D lightmapTex = lightmap.lightmapLight;

		// Handle UV lightmaps
		MeshFilter meshfilter = tr.GetComponent<MeshFilter>();
		if (meshfilter)
		{
			GlTF_Accessor lightmapUVAccessor = new GlTF_Accessor(GlTF_Accessor.GetNameFromObject(GetMesh(tr), "uv4"), GlTF_Accessor.Type.VEC2, GlTF_Accessor.ComponentType.FLOAT);
			lightmapUVAccessor.bufferView = GlTF_Writer.vec2BufferView;
			GlTF_Writer.accessors.Add(lightmapUVAccessor);
			Vector4 scaleOffset = meshRenderer.lightmapScaleOffset;
			lightmapUVAccessor.scaleValues = new Vector2(scaleOffset[0], scaleOffset[1]);
			lightmapUVAccessor.offsetValues = new Vector2(scaleOffset[2], scaleOffset[3]);
			primitive.attributes.lightmapTexCoordAccessor = lightmapUVAccessor;
		}

		string lightmapTexName = GlTF_Texture.GetNameFromObject(lightmapTex);
		if(!GlTF_Writer.textureNames.Contains(lightmapTexName))
		{
			//Generate lightmap
			Texture2D convertedLightmap = new Texture2D(lightmapTex.width, lightmapTex.height, TextureFormat.RGB24, false);
			Color[] lightmapPixels;
			getPixelsFromTexture(ref lightmapTex, out lightmapPixels);

			convertedLightmap.SetPixels(lightmapPixels);
			convertedLightmap.Apply();
			string filename = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(lightmapTex)) + ".jpg";
			string filepath = savedPath + "/" + filename;
			byte[] lightmapData = convertedLightmap.EncodeToJPG();
			File.WriteAllBytes(filepath, lightmapData);
			GlTF_Writer.exportedFiles.Add(filepath);
			GlTF_Image lightmapImg = new GlTF_Image();
			lightmapImg.name = GlTF_Image.GetNameFromObject(lightmapTex);
			lightmapImg.uri = filename;

			GlTF_Texture lmTex = new GlTF_Texture();
			lmTex.name = lightmapTexName;

			var valLightmap = new GlTF_Material.DictValue();
			valLightmap.name = "aoTexture";
			valLightmap.intValue.Add("texture", GlTF_Writer.textures.IndexOf(lmTex));
			valLightmap.stringValue.Add("semantic", "TEXCOORD_4");
			material.values.Add(valLightmap);

			// Both images use the same sampler
			GlTF_Sampler sampler;
			var samplerName = GlTF_Sampler.GetNameFromObject(lightmapTex);
			if (!GlTF_Writer.samplerNames.Contains(samplerName))
			{
				sampler = new GlTF_Sampler(lightmapTex);
				sampler.name = samplerName;
				GlTF_Writer.samplers.Add(sampler);
				GlTF_Writer.samplerNames.Add(samplerName);
			}

			lmTex.samplerIndex = GlTF_Writer.samplerNames.IndexOf(samplerName);
			GlTF_Writer.textureNames.Add(lmTex.name);
			GlTF_Writer.textures.Add(lmTex);

			lmTex.source = GlTF_Writer.imageNames.Count;
			GlTF_Writer.imageNames.Add(lightmapImg.name);
			GlTF_Writer.images.Add(lightmapImg);

			return new KeyValuePair<GlTF_Texture, GlTF_Image>(lmTex, lightmapImg);
		}
		else
		{
			var valLightmap = new GlTF_Material.DictValue();
			valLightmap.name = "aoTexture";
			valLightmap.intValue.Add("texture", GlTF_Writer.textureNames.IndexOf(lightmapTexName));
			valLightmap.stringValue.Add("semantic", "TEXCOORD_4");
			material.values.Add(valLightmap);
		}
		return lightmapKV;
	}

	private string toGlTFname(string name)
	{
		// remove spaces and illegal chars, replace with underscores
		string correctString = name.Replace(" ", "_");
		// make sure it doesn't start with a number
		return correctString;
	}

	private bool isInheritedFrom (Type t, Type baseT)
	{
		if (t == baseT)
			return true;
		t = t.BaseType;
		while (t != null && t != typeof(System.Object))
		{
			if (t == baseT)
				return true;
			t = t.BaseType;
		}
		return false;
	}

	private Renderer GetRenderer(Transform tr)
	{
		Renderer mr = tr.GetComponent<MeshRenderer>();
		if (mr == null) {
			mr = tr.GetComponent<SkinnedMeshRenderer>();
		}
		return mr;
	}

	private Mesh GetMesh(Transform tr)
	{
		var mr = GetRenderer(tr);
		Mesh m = null;
		if (mr != null)
		{
			var t = mr.GetType();
			if (t == typeof(MeshRenderer))
			{
				MeshFilter mf = tr.GetComponent<MeshFilter>();
				m = mf.sharedMesh;
			} else if (t == typeof(SkinnedMeshRenderer))
			{
				SkinnedMeshRenderer smr = mr as SkinnedMeshRenderer;
				m = smr.sharedMesh;
			}
		}
		return m;
	}

	// Convert unity Material to glTF PBR Material
	private void unityToPBRMaterial(Material mat, ref GlTF_Material material, bool doConvertImages = false)
	{
		Shader s = mat.shader;
		int spCount2 = ShaderUtil.GetPropertyCount(s);
		Dictionary<string, string> workflowChannelMap = UnityToPBRMetalChannel;
		bool isMaterialPBR = true;
		bool hasPBRMap = false;
		bool usePBRTextureAlpha = false;
		bool isMetal = true;

		if (!mat.shader.name.Contains("Standard"))
		{
			Debug.Log("Material " + mat.shader + " is not fully supported");
			isMaterialPBR = false;
		}

		if (isMaterialPBR)
		{
			// Is metal workflow used
			isMetal = mat.shader.name == "Standard";
			material.materialModel = isMetal ? "PBR_metal_roughness" : "PBR_specular_glossiness";

			// Is smoothness is defined by diffuse/albedo alpha or metal/specular texture alpha
			usePBRTextureAlpha = mat.GetFloat("_SmoothnessTextureChannel") == 0;
			workflowChannelMap = isMetal ? UnityToPBRMetalChannel : UnityToPBRSpecularChannel;
			hasPBRMap = (!isMetal && mat.GetTexture("_SpecGlossMap") != null || isMetal && mat.GetTexture("_MetallicGlossMap") != null);
		}

		for (var j = 0; j < spCount2; ++j)
		{
			var pName = ShaderUtil.GetPropertyName(s, j);
			var pType = ShaderUtil.GetPropertyType(s, j);
			// Smoothness factor is given by glossMapScale if there is a MetalGloss/SpecGloss texture, glossiness otherwise
			if (pName == "_Glossiness" && hasPBRMap || pName == "_GlossMapScale" && !hasPBRMap)
				continue;

			if (pType == ShaderUtil.ShaderPropertyType.Color && workflowChannelMap.ContainsKey(pName))
			{
				var matCol = new GlTF_Material.ColorValue();
				matCol.name = workflowChannelMap[pName];
				matCol.color = mat.GetColor(pName);
				//FIXME: Unity doesn't use albedo color when there is no specular texture
				if (pName.CompareTo("_SpecColor") == 0)
				{
					matCol.color.a = 1.0f;
				}

				material.values.Add(matCol);
			}
			else if (pType == ShaderUtil.ShaderPropertyType.Vector && workflowChannelMap.ContainsKey(pName))
			{
				var matVec = new GlTF_Material.VectorValue();
				matVec.name = workflowChannelMap[pName];
				matVec.vector = mat.GetVector(pName);
				material.values.Add(matVec);
			}
			else if ((pType == ShaderUtil.ShaderPropertyType.Float ||
				pType == ShaderUtil.ShaderPropertyType.Range) && workflowChannelMap.ContainsKey(pName))
			{
				var matFloat = new GlTF_Material.FloatValue();
				matFloat.name = workflowChannelMap[pName];
				matFloat.value = mat.GetFloat(pName);
				if (isMetal && (pName.CompareTo("_GlossMapScale") == 0 || pName.CompareTo("_Glossiness") == 0)) matFloat.value = 1 - matFloat.value;
				material.values.Add(matFloat);
			}
			else if (pType == ShaderUtil.ShaderPropertyType.TexEnv && workflowChannelMap.ContainsKey(pName))
			{
				var td = ShaderUtil.GetTexDim(s, j);
				if (td == UnityEngine.Rendering.TextureDimension.Tex2D)
				{
					var t = mat.GetTexture(pName);
					if (t != null)
					{
						Texture2D t2d = t as Texture2D;

						// These textures need split
						// FIXME: Should check and avoid to split two times the same textures

						//FIXME: two samplers are generated here. Should only have one
						if (isMaterialPBR && usePBRTextureAlpha && (((pName.CompareTo("_SpecGlossMap") == 0 || pName.CompareTo("_MetallicGlossMap") == 0)) || !usePBRTextureAlpha && pName.CompareTo("_MainTex") == 0))
						{
							// Split PBR texture into two textures (RGB => metal/specular and A => roughness)
							// Output two textures and two images
							List<KeyValuePair<GlTF_Texture, GlTF_Image>> outputs = splitPBRTexture(t2d, savedPath, isMetal);
							GlTF_Texture pbrTex = outputs[0].Key;
							GlTF_Texture roughnessTex = outputs[1].Key;
							GlTF_Image pbrImg = outputs[0].Value;
							GlTF_Image roughnessImage = outputs[1].Value;


							// Both images use the same sampler
							GlTF_Sampler sampler;
							var samplerName = GlTF_Sampler.GetNameFromObject(t);
							if (!GlTF_Writer.samplerNames.Contains(samplerName))
							{
								sampler = new GlTF_Sampler(t);
								sampler.name = samplerName;
								GlTF_Writer.samplers.Add(sampler);
								GlTF_Writer.samplerNames.Add(samplerName);
							}

							// Set the samplers (WHY ?!)
							pbrTex.samplerIndex = GlTF_Writer.samplerNames.IndexOf(samplerName);
							roughnessTex.samplerIndex = pbrTex.samplerIndex;

							// Add textures
							if (!GlTF_Writer.textureNames.Contains(pbrTex.name))
							{
								GlTF_Writer.textures.Add(pbrTex);
								GlTF_Writer.textureNames.Add(pbrTex.name);
								pbrTex.source = GlTF_Writer.images.Count;
								GlTF_Writer.imageNames.Add(pbrImg.name);
								GlTF_Writer.images.Add(pbrImg);
							}

							if (!GlTF_Writer.textureNames.Contains(roughnessTex.name))
							{
								GlTF_Writer.textures.Add(roughnessTex);
								GlTF_Writer.textureNames.Add(roughnessTex.name);
								roughnessTex.source = GlTF_Writer.images.Count;
								GlTF_Writer.imageNames.Add(roughnessImage.name);
								GlTF_Writer.images.Add(roughnessImage);
							}

							var valPBR = new GlTF_Material.IntValue();
							valPBR.name = workflowChannelMap[pName];
							valPBR.value = GlTF_Writer.textures.IndexOf(pbrTex);

							var valRoughness = new GlTF_Material.IntValue();
							valRoughness.name = isMetal ? "roughnessTexture" : "glossinessTexture";
							valRoughness.value = GlTF_Writer.textures.IndexOf(roughnessTex);

							// Add images to the collection
							material.values.Add(valPBR);
							material.values.Add(valRoughness);
						}
						else
						{
							bool isBumpTexture = pName.CompareTo("_BumpMap") == 0;
							bool isBumpMap = false;
							var texName = GlTF_Texture.GetNameFromObject(t);

							IMAGETYPE format = doConvertImages ? IMAGETYPE.RGB : IMAGETYPE.IGNORE;

							// Force psd conversion
							if(t2d != null)
							{
								string ext = Path.GetExtension(AssetDatabase.GetAssetPath(t2d));
								if(ext == ".psd")
									format = IMAGETYPE.RGB;
							}

							// Handle transparency
							if (pName.CompareTo("_MainTex") == 0 && mat.HasProperty("_Mode") && mat.GetFloat("_Mode") != 0)
							{
								string mode = mat.GetFloat("_Mode") == 1 ? "alphaMask" : "alphaBlend";
								material.extraString.Add("blendMode", mode);
								material.extraFloat.Add("cutoff", mat.GetFloat("_Cutoff"));

								if(doConvertImages)
									format = IMAGETYPE.RGBA;
							}
							var val = new GlTF_Material.IntValue();
							if (isBumpTexture)
							{
								TextureImporter im = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t)) as TextureImporter;
								isBumpMap = im.convertToNormalmap;
								val.name = isBumpMap ? "bumpTexture" : "normalTexture";
							}
							else
							{
								val.name = workflowChannelMap[pName];
							}


							if (!GlTF_Writer.textureNames.Contains(texName) && AssetDatabase.GetAssetPath(t).Length > 0)
							{
								if (doConvertImages && isBumpTexture && !isBumpMap)
								{
									format = IMAGETYPE.NORMAL_MAP;
								}

								var texPath = ExportTexture(t, savedPath, false, format);

								GlTF_Texture texture = new GlTF_Texture();
								texture.name = texName;

								GlTF_Image img = new GlTF_Image();
								img.name = GlTF_Image.GetNameFromObject(t);
								img.uri = texPath;
								texture.source = GlTF_Writer.imageNames.Count;
								GlTF_Writer.imageNames.Add(img.name);
								GlTF_Writer.images.Add(img);

								GlTF_Sampler sampler;
								var samplerName = GlTF_Sampler.GetNameFromObject(t);
								if (!GlTF_Writer.samplerNames.Contains(samplerName))
								{
									sampler = new GlTF_Sampler(t);
									sampler.name = samplerName;
									GlTF_Writer.samplers.Add(sampler);
									GlTF_Writer.samplerNames.Add(samplerName);
								}

								texture.samplerIndex = GlTF_Writer.samplerNames.IndexOf(samplerName);

								if(pName.CompareTo("_BumpMap") == 0 && !isBumpMap)
									texture.extraBool.Add("yUp", true);

								val.value = GlTF_Writer.textures.Count;
								material.values.Add(val);
								GlTF_Writer.textures.Add(texture);
								GlTF_Writer.textureNames.Add(texName);
							}
							else
							{
								val.value = GlTF_Writer.textures.Count - 1;
								material.values.Add(val);
							}

							if (AssetDatabase.GetAssetPath(t).Length == 0)
							{
								Debug.LogWarning("Texture '" + t.name + "' has not been exported (Asset not found)");
							}
						}
					}
				}
			}
		}
	}

	private List<KeyValuePair<GlTF_Texture, GlTF_Image>> splitPBRTexture(Texture2D texture, string path, bool isMetal)
	{
		List<KeyValuePair<GlTF_Texture, GlTF_Image>> outputs = new List<KeyValuePair<GlTF_Texture, GlTF_Image>>();
		GlTF_Texture pbrTex = new GlTF_Texture();
		GlTF_Texture roughnessTex = new GlTF_Texture();

		string pbrSuffix = isMetal ? "_metallic" : "_specular";
		string smoothSuffix = isMetal ? "_roughness" : "_glossiness";

		pbrTex.name = GlTF_Texture.GetNameFromObject(texture) + pbrSuffix;
		roughnessTex.name = GlTF_Texture.GetNameFromObject(texture) + smoothSuffix;

		Texture2D pbr = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
		Texture2D roughness = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);

		// Need to make texture readable
		Color[] pixels;
		getPixelsFromTexture(ref texture, out pixels);

		//FIXME should not be done like this..
		if(pbr==null)
			pbr = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);

		pbr.SetPixels(pixels);
		pbr.Apply();

		Color[] alphaRgbPixels = new Color[pixels.Length];
		for(int i=0;i < pixels.Length; ++i)
		{
			float val = isMetal ? 1 - pixels[i].a : pixels[i].a;
			alphaRgbPixels[i] = new Color(val, val, val);
		}

		//FIXME should not be done like this..
		if (roughness == null)
			roughness = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);

		roughness.SetPixels(alphaRgbPixels);
		roughness.Apply();

		var pbrData = pbr.EncodeToJPG(jpgQuality);
		var roughnessData = roughness.EncodeToJPG(jpgQuality);

		var assetPath = AssetDatabase.GetAssetPath(texture);
		var filename = Path.GetFileName(assetPath);
		string fileNameBase = Path.GetFileNameWithoutExtension(filename);

		// Convert spaces into underscores
		fileNameBase = toGlTFname(fileNameBase);

		string fileNamePbr = fileNameBase + pbrSuffix + ".jpg";
		string filenameRough = fileNameBase + smoothSuffix + ".jpg";

		// Write PBR map
		string pbrPath = path + "/" + fileNamePbr;
		string roughPath = path + "/" + filenameRough;

		File.WriteAllBytes(pbrPath, pbrData);
		File.WriteAllBytes(roughPath, roughnessData);

		if (GlTF_Writer.exportedFiles.Contains(pbrPath) == false)
			GlTF_Writer.exportedFiles.Add(pbrPath);

		if (GlTF_Writer.exportedFiles.Contains(roughPath) == false)
			GlTF_Writer.exportedFiles.Add(roughPath);

		GlTF_Image pbrImg = new GlTF_Image();
		GlTF_Image roughnessImg = new GlTF_Image();
		pbrImg.name = GlTF_Image.GetNameFromObject(texture) + pbrSuffix;
		pbrImg.uri = fileNamePbr;
		roughnessImg.name = GlTF_Image.GetNameFromObject(texture) + smoothSuffix;
		roughnessImg.uri = filenameRough;

		outputs.Add(new KeyValuePair<GlTF_Texture, GlTF_Image>(pbrTex, pbrImg));
		outputs.Add(new KeyValuePair<GlTF_Texture, GlTF_Image>(roughnessTex, roughnessImg));

		return outputs;
	}

	void populateTechniqueFromShader(Shader s, GlTF_Technique tech)
	{
		GlTF_Technique.Parameter tParam;
		int spCount = ShaderUtil.GetPropertyCount(s);
		bool isMetal = s.name == "Standard";
		Dictionary<string, string> workflowChannelMap = isMetal ? UnityToPBRMetalChannel : UnityToPBRSpecularChannel;

		for (var j = 0; j < spCount; ++j)
		{
			var pName = ShaderUtil.GetPropertyName(s, j);
			var pType = ShaderUtil.GetPropertyType(s, j);

			GlTF_Technique.Uniform tUni;
			if (pType == ShaderUtil.ShaderPropertyType.Color && exportPBRMaterials == workflowChannelMap.ContainsKey(pName))
			{
				tParam = new GlTF_Technique.Parameter();
				tParam.name = workflowChannelMap[pName];
				tParam.type = GlTF_Technique.Type.FLOAT_VEC4;
				tech.parameters.Add(tParam);
				tUni = new GlTF_Technique.Uniform();
				tUni.name = pName;
				tUni.param = tParam.name;
				tech.uniforms.Add(tUni);
			}
			else if (pType == ShaderUtil.ShaderPropertyType.Vector && exportPBRMaterials == workflowChannelMap.ContainsKey(pName))
			{
				tParam = new GlTF_Technique.Parameter();
				tParam.name = workflowChannelMap[pName];
				tParam.type = GlTF_Technique.Type.FLOAT_VEC4;
				tech.parameters.Add(tParam);
				tUni = new GlTF_Technique.Uniform();
				tUni.name = pName;
				tUni.param = tParam.name;
				tech.uniforms.Add(tUni);
			}
			else if ((pType == ShaderUtil.ShaderPropertyType.Float ||
				pType == ShaderUtil.ShaderPropertyType.Range) && exportPBRMaterials == workflowChannelMap.ContainsKey(pName))
			{
				tParam = new GlTF_Technique.Parameter();
				tParam.name = workflowChannelMap[pName];
				tParam.type = GlTF_Technique.Type.FLOAT;
				tech.parameters.Add(tParam);
				tUni = new GlTF_Technique.Uniform();
				tUni.name = pName;
				tUni.param = tParam.name;
				tech.uniforms.Add(tUni);
			}
			else if (pType == ShaderUtil.ShaderPropertyType.TexEnv && exportPBRMaterials == workflowChannelMap.ContainsKey(pName))
			{
				var td = ShaderUtil.GetTexDim(s, j);
				if (td == UnityEngine.Rendering.TextureDimension.Tex2D)
				{
					tParam = new GlTF_Technique.Parameter();
					tParam.name = workflowChannelMap[pName];
					tParam.type = GlTF_Technique.Type.SAMPLER_2D;
					tech.parameters.Add(tParam);
					tUni = new GlTF_Technique.Uniform();
					tUni.name = pName;
					tUni.param = tParam.name;
					tech.uniforms.Add(tUni);
				}
			}
		}
	}

	private void getPixelsFromTexture(ref Texture2D texture, out Color[] pixels)
	{
		//Make texture readable
		TextureImporter im = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;
		bool readable = im.isReadable;
		if (!readable)
		{
			im.isReadable = true;
			im.SaveAndReimport();
		}

		pixels = texture.GetPixels();

		if (!readable)
		{
			im.isReadable = false;
			im.SaveAndReimport();
		}
	}

	private void getBytesFromTexture(ref Texture2D texture, out byte[] pixels, IMAGETYPE imageFormat)
	{
		//Make texture readable
		TextureImporter im = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;
		bool readable = im.isReadable;
		TextureImporterCompression format = im.textureCompression;
		TextureImporterType type = im.textureType;

		if (!readable)
			im.isReadable = true;
		if (type != TextureImporterType.Default)
			im.textureType = TextureImporterType.Default;

		im.textureCompression = TextureImporterCompression.Uncompressed;
		im.SaveAndReimport();

		if (imageFormat == IMAGETYPE.RGBA || imageFormat == IMAGETYPE.NORMAL_MAP)
			pixels = texture.EncodeToPNG();
		else
			pixels = texture.EncodeToJPG(jpgQuality);

		if (!readable)
			im.isReadable = false;
		if (type != TextureImporterType.Default)
			im.textureType = type;

		im.textureCompression = format;
		im.SaveAndReimport();
	}

	//public static void convertImages(ref List<GlTF_Image> images, ref List<string> exportedFiles, string path)
	//{
	//    TextureConverter converter = new TextureConverter();
	//    //converter.init();
	//    foreach (GlTF_Image img in images)
	//    {
	//        // uri contains only the filename, not the full path
	//        string uri = converter.convert(path, img);
	//        string inputPath = path + "/" + img.uri;
	//        string outputPath = path + "/" + uri;
	//        if (uri.Length > 0 && File.Exists(outputPath))
	//        {
	//            File.Delete(inputPath);
	//            exportedFiles.Remove(inputPath);
	//            img.uri = uri;
	//            exportedFiles.Add(outputPath);
	//        }
	//    }
	//}

	// Return texture filename with good extension
	//   public string convertNormalMap(string assetPath, string outputDir)
	//{
	//	string filename = Path.GetFileNameWithoutExtension(assetPath) + ".png";
	//	string outputPath = Path.Combine(outputDir, filename);

	//	System.Drawing.Image img = System.Drawing.Image.FromFile(assetPath);
	//	System.Drawing.Image target = new System.Drawing.Bitmap(img);

	//	target.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
	//	target.Dispose();

	//	GlTF_Writer.exportedFiles.Add(outputPath);
	//	return filename;
	//}

	public string convertTexture(Texture2D inputTexture, string assetPath, string outputDir, IMAGETYPE format)
	{
		Color[] pixels;
		getPixelsFromTexture(ref inputTexture, out pixels);

		//byte[] outputData = format == IMAGETYPE.RGB ? tex.EncodeToJPG(jpgQuality) : tex.EncodeToPNG();
		byte[] outputData;
		getBytesFromTexture(ref inputTexture, out outputData, format);
		string outputFilename = Path.GetFileNameWithoutExtension(assetPath) + (format == IMAGETYPE.RGB ? ".jpg" : ".png");
		string outputPath = Path.Combine(outputDir,outputFilename);

		File.WriteAllBytes(outputPath, outputData);
		GlTF_Writer.exportedFiles.Add(outputPath);

		return outputFilename;
	}

	private string ExportTexture(Texture texture, string path, bool forceRGBA32=false, IMAGETYPE format=IMAGETYPE.IGNORE)
	{
		var assetPath = AssetDatabase.GetAssetPath(texture);
		var fn = Path.GetFileName(assetPath);
		var t = texture as Texture2D;
		if (t != null)
		{
			if (forceRGBA32 && t.format != TextureFormat.RGBA32)
			{
				fn = Path.GetFileNameWithoutExtension(assetPath) + ".png";
				var dstPath = Path.Combine(path, fn);
				//dstPath = toGlTFname(dstPath);
				Texture2D t2 = new Texture2D(t.width, t.height, TextureFormat.RGBA32, false);
				t2.SetPixels(t.GetPixels());
				t2.Apply();
				var b = t2.EncodeToPNG();
				File.WriteAllBytes(dstPath, b);
				GlTF_Writer.exportedFiles.Add(dstPath);
			}
			//else if(format == IMAGETYPE.NORMAL_MAP)
			//{
			//	return convertNormalMap(assetPath, path);
			//}
			else if (format != IMAGETYPE.IGNORE)
			{
				return convertTexture(t, assetPath, path, format);
			}
			else
			{
				string dstPath = Path.Combine(path,fn);
				File.Copy(assetPath, dstPath, true);
				GlTF_Writer.exportedFiles.Add(dstPath);
			}
		}
		return fn;
	}
}
