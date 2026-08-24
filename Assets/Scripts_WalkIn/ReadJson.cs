using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization;
using UnityEngine;
using Newtonsoft.Json;


[System.Serializable]
public class PythonPoint
{
    public float x;
    public float y;
}

[System.Serializable]
public class PythonTransform
{
    public float x;
    public float y;
}

[System.Serializable]
public class PolygonData
{
    public PythonPoint centroid;
    public PythonPoint final_centroid;
    public PythonTransform trans;
    public float rotation;
    public float[][] coords;
}

[Serializable]
public class TraverseData
{
    private List<List<float[]>> _coordsList;
    private List<float[]> _simpleCoords;
    private bool _isNested;

    [JsonProperty("coords")]
    private JArray _jsonCoords;

    [JsonIgnore]
    public bool IsNested => _isNested;

    [JsonIgnore]
    public List<List<float[]>> NestedCoords => _coordsList;

    [JsonIgnore]
    public List<float[]> SimpleCoords => _simpleCoords;

    [OnDeserialized]
    private void OnDeserialized(StreamingContext context)
    {
        if (_jsonCoords == null) return;

        // Check if it's a nested structure by looking at the first element
        var firstElement = _jsonCoords.First;
        _isNested = firstElement != null && firstElement.Type == JTokenType.Array &&
                    firstElement.First != null && firstElement.First.Type == JTokenType.Array;

        if (_isNested)
        {
            _coordsList = _jsonCoords.ToObject<List<List<float[]>>>();
            _simpleCoords = null;
        }
        else
        {
            _simpleCoords = _jsonCoords.ToObject<List<float[]>>();
            _coordsList = null;
        }
    }
}


[System.Serializable]
public class PythonEachHouse
{
    public string name;
    public PolygonData polygon;
    public PolygonData boundary;
    public TraverseData traverse;
}

[System.Serializable]
public class PythonAllHouse
{
    public List<PythonEachHouse> items;
}


public class ReadJson : MonoBehaviour
{




    void Update()
    {


    }



}
