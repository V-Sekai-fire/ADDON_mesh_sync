#if UNITY_STANDALONE
using System;
#endif
using Unity.Collections;
using UnityEngine;
using UnityEngine.Serialization;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Unity.MeshSync {

/// <summary>
/// A component to sync meshes/models editing in DCC tools into Unity in real time.
/// </summary>
[ExecuteInEditMode]
public class MeshSyncServer : BaseMeshSync {

    /// <summary>
    /// Callback which will be called after MeshSyncServer receives data and finishes processing it
    /// </summary>
    public ServerMessageCallback OnPostRecvMessageCallback = null;
    
//----------------------------------------------------------------------------------------------------------------------
    
    
    private protected override void InitInternalV() {
        
    }


    private protected override void UpdateMaterialAssetV(MaterialData materialData) {
        UpdateMaterialAssetByDefault(materialData,m_config.GetModelImporterSettings());
    }
    
//----------------------------------------------------------------------------------------------------------------------        
    
#region Getter/Setter
    internal bool IsServerStarted()             { return m_serverStarted;}
    internal bool IsAutoStart()                 { return m_autoStartServer; }
    internal int  GetServerPort()               { return m_serverPort; }
    internal void SetServerPort(int port )      { m_serverPort = port; }
    
    internal override MeshSyncPlayerConfig GetConfigV() => m_config;
    
#endregion

//----------------------------------------------------------------------------------------------------------------------        
    /// <summary>
    /// Sets whether the server should be started automatically or not
    /// </summary>
    /// <param name="autoStart">true if the server should start automatically; otherwise, false.</param>
    public void SetAutoStartServer(bool autoStart) {
        m_autoStartServer = autoStart; 

#if UNITY_STANDALONE        
        if (m_autoStartServer && !m_serverStarted) {
            StartServer();
        }
#endif
    }
    
//----------------------------------------------------------------------------------------------------------------------        
    
#if UNITY_EDITOR
    internal bool foldServerSettings
    {
        get { return m_foldServerSettings; }
        set { m_foldServerSettings = value; }
    }
#endif

//----------------------------------------------------------------------------------------------------------------------        


    internal bool DoesServerAllowPublicAccess() {
        bool ret = false;
#if UNITY_STANDALONE            
        ret = m_server.IsPublicAccessAllowed();
#endif
        return ret;
    }
    
    /// <summary>
    /// Starts the server. If the server is already running, it will be restarted.
    /// </summary>
    public void StartServer()
    {
#if UNITY_STANDALONE            
        StopServer();

#if UNITY_EDITOR 
        //Deploy HTTP assets to StreamingAssets
        DeployStreamingAssets.Deploy();
#endif
        MeshSyncProjectSettings projectSettings = MeshSyncProjectSettings.GetOrCreateInstance();
        
        
        m_serverSettings.port = (ushort)m_serverPort;
        m_serverSettings.zUpCorrectionMode = (ZUpCorrectionMode) m_config.ZUpCorrection;

        m_server = Server.Start(ref m_serverSettings);
        m_server.fileRootPath = GetServerDocRootPath();
        m_server.AllowPublicAccess(projectSettings.GetServerPublicAccess());
        
        m_handler = HandleRecvMessage;

#if UNITY_EDITOR
        EditorApplication.update += PollServerEvents;
#endif
        if (m_config.Logging)
            Debug.Log("[MeshSync] Server started (port: " + m_serverSettings.port + ")");

        m_serverStarted = true;
#else
        Debug.LogWarning("[MeshSync] Server functions are not supported in non-Standalone platform");
#endif //UNITY_STANDALONE
    }

//----------------------------------------------------------------------------------------------------------------------        

    internal void StopServer() {
#if UNITY_STANDALONE            
        if (!m_server) 
            return;
        
#if UNITY_EDITOR
        EditorApplication.update -= PollServerEvents;
#endif
        m_server.Stop();
        m_server = default(Server);

        if (m_config.Logging)
            Debug.Log("[MeshSync] Server stopped (port: " + m_serverSettings.port + ")");

        m_serverStarted = false;
#else
        Debug.LogWarning("[MeshSync] Server functions are not supported in non-Standalone platform");
#endif //UNITY_STANDALONE
    }

//----------------------------------------------------------------------------------------------------------------------

    private protected override void OnBeforeSerializeMeshSyncPlayerV() {
        
    }

    private protected override void OnAfterDeserializeMeshSyncPlayerV() {
        m_serverVersion = CUR_SERVER_VERSION;
        
        if (string.IsNullOrEmpty(GetAssetsFolder())) {
            SetAssetsFolder(MeshSyncConstants.DEFAULT_ASSETS_PATH);
        }
        
    }   
    
//----------------------------------------------------------------------------------------------------------------------
    

#if UNITY_STANDALONE
    #region Impl
    
    void CheckParamsUpdated()  {

        if (m_server) {
            m_server.zUpCorrectionMode = (ZUpCorrectionMode) m_config.ZUpCorrection;
        }
    }
    #endregion

    #region MessageHandlers

    private void PollServerEvents() {
        if (m_requestRestartServer) {
            m_requestRestartServer = false;
            StartServer();
        }
        if (m_captureScreenshotInProgress) {
            m_captureScreenshotInProgress = false;
            m_server.screenshotPath = "screenshot.png";
        }

        if (m_server.numMessages > 0)
            m_server.ProcessMessages(m_handler);
    }

    void HandleRecvMessage(NetworkMessageType type, IntPtr data) {
        Try(() => {
            switch (type) {
                case NetworkMessageType.Get:
                    OnRecvGet((GetMessage)data);
                    break;
                case NetworkMessageType.Set:
                    OnRecvSet((SetMessage)data);
                    break;
                case NetworkMessageType.Delete:
                    OnRecvDelete((DeleteMessage)data);
                    break;
                case NetworkMessageType.Fence:
                    OnRecvFence((FenceMessage)data);
                    break;
                case NetworkMessageType.Text:
                    OnRecvText((TextMessage)data);
                    break;
                case NetworkMessageType.Screenshot:
                    OnRecvScreenshot(data);
                    break;
                case NetworkMessageType.Query:
                    OnRecvQuery((QueryMessage)data);
                    break;
                default:
                    break;
            }
        });

        OnPostRecvMessageCallback?.Invoke(type);
    }

    void OnRecvGet(GetMessage mes) {
        m_server.BeginServe();
        foreach (Renderer mr in FindObjectsOfType<Renderer>())
            ServeMesh(mr, mes);
        foreach (MaterialHolder mat in m_materialList)
            ServeMaterial(mat.material, mes);
        m_server.EndServe();

        if (m_config.Logging)
            Debug.Log("[MeshSync] served");
    }

    void OnRecvDelete(DeleteMessage mes) {

        int numInstanceMeshes = mes.numInstances;
        for (int i = 0; i < numInstanceMeshes; i++)
        {
            var instance = mes.GetInstance(i);
            EraseInstanceInfoRecord(instance);
            EraseInstancedEntityRecord(instance);
        }

        int numEntities = mes.numEntities;
        for (int i = 0; i < numEntities; ++i)
            EraseEntityRecord(mes.GetEntity(i));

        int numMaterials = mes.numMaterials;
        for (int i = 0; i < numMaterials; ++i)
            EraseMaterialRecord(mes.GetMaterial(i).id);
    }

    void OnRecvFence(FenceMessage mes) {
        if (mes.type == FenceMessage.FenceType.SceneBegin) {
            BeforeUpdateScene();
        } else if (mes.type == FenceMessage.FenceType.SceneEnd) {
            AfterUpdateScene();
            m_server.NotifyPoll(PollMessage.PollType.SceneUpdate);
        }
    }

    static void OnRecvText(TextMessage mes) {
        mes.Print();
    }

    void OnRecvSet(SetMessage mes) {
        UpdateScene(mes.scene, true);
    }

    void OnRecvScreenshot(IntPtr data) {
        ForceRepaint();

        ScreenCapture.CaptureScreenshot("screenshot.png");
        // actual capture will be done at end of frame. not done immediately.
        // just set flag now.
        m_captureScreenshotInProgress = true;
    }

    void OnRecvQuery(QueryMessage data) {
        switch (data.queryType) {
            case QueryMessage.QueryType.PluginVersion:
                data.AddResponseText(Lib.GetPluginVersion());
                break;
            case QueryMessage.QueryType.ProtocolVersion:
                data.AddResponseText(Lib.GetProtocolVersion().ToString());
                break;
            case QueryMessage.QueryType.HostName:
                data.AddResponseText("Unity " + Application.unityVersion);
                break;
            case QueryMessage.QueryType.RootNodes:
                {
                    GameObject[] roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
                    foreach (GameObject go in roots)
                        data.AddResponseText(BuildPath(go.transform));
                }
                break;
            case QueryMessage.QueryType.AllNodes: {
                Transform[] objTransforms = FindObjectsOfType<Transform>();
                foreach (Transform t in objTransforms)
                    data.AddResponseText(BuildPath(t));
                break;
            }
            default:
                break;
        }
        data.FinishRespond();
    }
    #endregion

    #region ServeScene
    bool ServeMesh(Renderer objRenderer, GetMessage mes) {
        bool ret = false;
        Mesh origMesh = null;

        MeshData dst = MeshData.Create();
        if (objRenderer.GetType() == typeof(MeshRenderer)){
            ret = CaptureMeshRenderer(ref dst, objRenderer as MeshRenderer, mes, ref origMesh);
        }
        else if (objRenderer.GetType() == typeof(SkinnedMeshRenderer)){
            ret = CaptureSkinnedMeshRenderer(ref dst, objRenderer as SkinnedMeshRenderer, mes, ref origMesh);
        }

        if (ret) {
            TransformData dstTrans = dst.transform;
            Transform rendererTransform = objRenderer.transform;
            dstTrans.hostID = GetObjectlID(objRenderer.gameObject);
            dstTrans.position = rendererTransform.localPosition;
            dstTrans.rotation = rendererTransform.localRotation;
            dstTrans.scale = rendererTransform.localScale;
            dst.local2world = rendererTransform.localToWorldMatrix;
            dst.world2local = rendererTransform.worldToLocalMatrix;

            EntityRecord rec;
            if (!m_hostObjects.TryGetValue(dstTrans.hostID, out rec)) {
                rec = new EntityRecord();
                m_hostObjects.Add(dstTrans.hostID, rec);
            }
            rec.go = objRenderer.gameObject;
            rec.origMesh = origMesh;

            dstTrans.path = BuildPath(rendererTransform);
            m_server.ServeMesh(dst);
        }
        return ret;
    }
    bool ServeTexture(Texture2D v, GetMessage mes) {
        TextureData data = TextureData.Create();
        data.name = v.name;
        // todo
        m_server.ServeTexture(data);
        return true;
    }
    bool ServeMaterial(Material mat, GetMessage mes) {
        MaterialData data = MaterialData.Create();
        data.name = mat.name;
        if (mat.HasProperty("_Color"))
            data.color = mat.GetColor("_Color");
        m_server.ServeMaterial(data);
        return true;
    }

    bool CaptureMeshRenderer(ref MeshData dst, MeshRenderer mr, GetMessage mes, ref Mesh mesh) {
        mesh = mr.GetComponent<MeshFilter>().sharedMesh;
        if (mesh == null) return false;
        if (!mesh.isReadable) {
            Debug.LogWarning("Mesh " + mr.name + " is not readable and be ignored");
            return false;
        }

        CaptureMesh(ref dst, mesh, null, mes.flags, mr.sharedMaterials);
        return true;
    }

    bool CaptureSkinnedMeshRenderer(ref MeshData dst, SkinnedMeshRenderer smr, GetMessage mes, ref Mesh mesh) {
        mesh = smr.sharedMesh;
        if (mesh == null) return false;

        if (!mes.bakeSkin && !mesh.isReadable) {
            Debug.LogWarning("Mesh " + smr.name + " is not readable and be ignored");
            return false;
        }

        Cloth cloth = smr.GetComponent<Cloth>();
        if (cloth != null && mes.bakeCloth) {
            CaptureMesh(ref dst, mesh, cloth, mes.flags, smr.sharedMaterials);
        }

        if (mes.bakeSkin) {
            Mesh tmp = new Mesh();
            smr.BakeMesh(tmp);
            CaptureMesh(ref dst, tmp, null, mes.flags, smr.sharedMaterials);
        } else {
            CaptureMesh(ref dst, mesh, null, mes.flags, smr.sharedMaterials);

            // bones
            if (mes.flags.getBones) {
                dst.SetBonePaths(this, smr.bones);
                dst.bindposes = mesh.bindposes;

                NativeArray<byte>        bonesPerVertex = mesh.GetBonesPerVertex();
                NativeArray<BoneWeight1> weights        = mesh.GetAllBoneWeights();
                dst.WriteBoneWeightsV(ref bonesPerVertex, ref weights);
            }

            // blendshapes
            if (mes.flags.getBlendShapes && mesh.blendShapeCount > 0) {
                Vector3[] v = new Vector3[mesh.vertexCount];
                Vector3[] n = new Vector3[mesh.vertexCount];
                Vector3[] t = new Vector3[mesh.vertexCount];
                for (int bi = 0; bi < mesh.blendShapeCount; ++bi) {
                    BlendShapeData bd = dst.AddBlendShape(mesh.GetBlendShapeName(bi));
                    bd.weight = smr.GetBlendShapeWeight(bi);
                    int frameCount = mesh.GetBlendShapeFrameCount(bi);
                    for (int fi = 0; fi < frameCount; ++fi) {
                        mesh.GetBlendShapeFrameVertices(bi, fi, v, n, t);
                        float w = mesh.GetBlendShapeFrameWeight(bi, fi);
                        bd.AddFrame(w, v, n, t);
                    }
                }
            }
        }
        return true;
    }

    void CaptureMesh(ref MeshData data, Mesh mesh, Cloth cloth, GetFlags flags, Material[] materials) {
        if (flags.getPoints)
            data.WritePoints(mesh.vertices);
        if (flags.getNormals)
            data.WriteNormals(mesh.normals);
        if (flags.getTangents)
            data.WriteTangents(mesh.tangents);
        
        //UV
        if (flags.GetUV(0)) { data.WriteUV(0, mesh.uv); }
        if (flags.GetUV(1)) { data.WriteUV(1, mesh.uv2); }
        if (flags.GetUV(2)) { data.WriteUV(2, mesh.uv3); }
        if (flags.GetUV(3)) { data.WriteUV(3, mesh.uv4); }
        if (flags.GetUV(4)) { data.WriteUV(4, mesh.uv5); }
        if (flags.GetUV(5)) { data.WriteUV(5, mesh.uv6); }
        if (flags.GetUV(6)) { data.WriteUV(6, mesh.uv7); }
        if (flags.GetUV(7)) { data.WriteUV(7, mesh.uv8); }
               
        if (flags.getColors)
            data.WriteColors(mesh.colors);
        if (flags.getIndices) {
            if (!flags.getMaterialIDs || materials == null || materials.Length == 0) {
                data.WriteIndices(mesh.triangles);
            } else {
                int n = mesh.subMeshCount;
                for (int i = 0; i < n; ++i) {
                    int[] indices = mesh.GetIndices(i);
                    int mid = i < materials.Length ? GetMaterialIndex(materials[i]) : 0;
                    data.WriteSubmeshTriangles(indices, mid);
                }
            }
        }

        // bones & blendshapes are handled by CaptureSkinnedMeshRenderer()
    }
    #endregion //ServeScene


    #region Events

#if UNITY_EDITOR
    void OnValidate() {
        CheckParamsUpdated();
    }

    void Reset() {
        MeshSyncProjectSettings projectSettings = MeshSyncProjectSettings.GetOrCreateInstance();
        m_config = new MeshSyncServerConfig(projectSettings.GetDefaultServerConfig());
        m_serverPort = projectSettings.GetDefaultServerPort();
        
    }
#endif


    private protected override void OnEnable() {
        base.OnEnable();
        if (m_autoStartServer) {
            m_requestRestartServer = true;
        }
    }

    private protected override void OnDisable() {
        base.OnDisable();
        StopServer();
    }

    void LateUpdate() {
        PollServerEvents();
    }
    #endregion

//----------------------------------------------------------------------------------------------------------------------


    ServerSettings m_serverSettings = ServerSettings.defaultValue;
    Server m_server;
    Server.MessageHandler m_handler;
    bool m_requestRestartServer = false;
    bool m_captureScreenshotInProgress = false;
    
#endif // UNITY_STANDALONE
    
    [SerializeField] private bool m_autoStartServer = false;
    [SerializeField] private int  m_serverPort      = MeshSyncConstants.DEFAULT_SERVER_PORT;
#if UNITY_EDITOR
    [SerializeField] bool m_foldServerSettings = true;
#endif
    
    [SerializeField] private MeshSyncServerConfig m_config;

#pragma warning disable 414
    //Renamed in 0.10.x-preview
    [FormerlySerializedAs("m_version")] [HideInInspector][SerializeField] private int m_serverVersion = (int) ServerVersion.NO_VERSIONING;
#pragma warning restore 414
    private const int CUR_SERVER_VERSION = (int) ServerVersion.INITIAL_0_4_0;
    
    
    private bool m_serverStarted = false;
    
//----------------------------------------------------------------------------------------------------------------------    
    
    enum ServerVersion {
        NO_VERSIONING = 0, //Didn't have versioning in earlier versions        
        INITIAL_0_4_0 = 1, //initial for version 0.4.0-preview 
    
    }
    
}

} //end namespace
