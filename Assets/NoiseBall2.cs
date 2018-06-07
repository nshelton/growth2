using UnityEngine;
using System.Runtime.InteropServices;

public class NoiseBall2 : MonoBehaviour
{
    #region structs

    private struct Edge
    {
        int a;
        int b;
    }

    #endregion

    #region Exposed attributes

    [SerializeField] int _maxNodes = 1000;

    [SerializeField] int _numNodes = 10;

    [SerializeField] float _scale = 0.1f;
    [SerializeField] float _growthRate = 0.01f;
    [SerializeField] float _maxSize = 0.5f;

    [SerializeField] Material _material;
    [SerializeField] Mesh _mesh;

    #endregion

    #region Hidden attributes

    [SerializeField, HideInInspector] ComputeShader _compute;

    #endregion

    #region Private fields

    ComputeBuffer _drawArgsBuffer;
    ComputeBuffer _nodePositionsRead, _nodePositionsWrite;
    ComputeBuffer _edgeListBuffer;
    MaterialPropertyBlock _props;

    private int _numEdges = 1;

    #endregion

    #region Compute configurations

    const int kThreadCount = 64;
    int ThreadGroupCount { get { return _maxNodes / kThreadCount; } }
    int NodeCount { get { return kThreadCount * ThreadGroupCount; } }
    int MaxEdges { get { return NodeCount; } }

    #endregion

    #region MonoBehaviour functions

    private void OnValidate()
    {
        _maxNodes = Mathf.Max(_maxNodes, kThreadCount);
        _numNodes = Mathf.Max(_numNodes, 2);
    }

    protected void SwapBuffer(ref ComputeBuffer buf0, ref ComputeBuffer buf1)
    {
        var tmp = buf0;
        buf0 = buf1;
        buf1 = tmp;
    }

    void Init()
    {
        Debug.Log("Init");
        if (_nodePositionsRead != null) _nodePositionsRead.Release();
        if (_nodePositionsWrite != null) _nodePositionsWrite.Release();
        if (_edgeListBuffer != null) _edgeListBuffer.Release();
        if (_drawArgsBuffer != null) _drawArgsBuffer.Release();

        _nodePositionsRead = new ComputeBuffer(_maxNodes, Marshal.SizeOf(typeof(Vector4)));
        _nodePositionsWrite = new ComputeBuffer(_maxNodes, Marshal.SizeOf(typeof(Vector4)));
        _edgeListBuffer = new ComputeBuffer(_maxNodes, Marshal.SizeOf(typeof(Edge)));

        _drawArgsBuffer = new ComputeBuffer(
            1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments
        );

        _numNodes = 2;
        _numEdges = 1;

        _drawArgsBuffer.SetData(new uint[5] {
            _mesh.GetIndexCount(0),
            (uint)NodeCount,
            0, 0, 0});

        // This property block is used only for avoiding a bug (issue #913828)
        _props = new MaterialPropertyBlock();
        _props.SetFloat("_UniqueID", Random.value);

        var initKernel = _compute.FindKernel("InitNodes");
        _compute.SetBuffer(initKernel, "NodeListWrite", _nodePositionsWrite);
        _compute.Dispatch(initKernel, ThreadGroupCount, 1, 1);
        // SwapBuffer(ref _nodePositionsRead, ref _nodePositionsWrite);

    }

    void OnDestroy()
    {
        _drawArgsBuffer.Release();
        if (_nodePositionsRead != null) _nodePositionsRead.Release();
        if (_nodePositionsWrite != null) _nodePositionsWrite.Release();
        if (_edgeListBuffer != null) _edgeListBuffer.Release();
    }

    void UpdateUniforms()
    {
        _compute.SetFloat("Time", Time.time);
        _compute.SetFloat("Scale", _scale);
        _compute.SetFloat("GrowthRate", _growthRate);
        _compute.SetFloat("MaxSize", _maxSize);
        _compute.SetInt("NumPoints", NodeCount);
        _compute.SetInt("NumEdges", _numEdges);
    }

    void Update()
    {

        if (_nodePositionsRead == null || _nodePositionsRead.count != NodeCount)
        {
            Init();
        }

        // Invoke the update compute kernel.
        var kernel = _compute.FindKernel("UpdateNodes");
        UpdateUniforms();
        _compute.SetBuffer(kernel, "NodeListRead", _nodePositionsRead);
        _compute.SetBuffer(kernel, "NodeListWrite", _nodePositionsWrite);
        _compute.Dispatch(kernel, ThreadGroupCount, 1, 1);

        


        SwapBuffer(ref _nodePositionsRead, ref _nodePositionsWrite);

        Render();
    }

    void Render()
    {
        _material.SetMatrix("_LocalToWorld", transform.localToWorldMatrix);
        _material.SetMatrix("_WorldToLocal", transform.worldToLocalMatrix);

        _material.SetBuffer("_NodeList", _nodePositionsRead);
        _material.SetFloat("_Scale", _scale);

        Graphics.DrawMeshInstancedIndirect(
            _mesh, 0, _material,
            new Bounds(transform.position, transform.lossyScale * 5),
            _drawArgsBuffer, 0, _props, 
            UnityEngine.Rendering.ShadowCastingMode.On, true
        );

    }
    #endregion
}
