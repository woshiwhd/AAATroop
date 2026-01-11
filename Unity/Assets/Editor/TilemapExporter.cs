using System;
using System.Collections.Generic;
using Script;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

public class TilemapExporter
{
    [Serializable]
    private class ChunkData
    {
        public int originX;
        public int originY;
        public int width;
        public int height;
        public int[] tiles;
        public byte[] blocking;
    }

    [MenuItem("Tools/Tilemap/Export Current Tilemap (Chunks 32)")]
    public static void ExportSelectedTilemap()
    {
        var go = Selection.activeGameObject;
        if (go == null)
        {
            Debug.LogWarning("Select a GameObject that has a Tilemap");
            return;
        }

        var tilemap = go.GetComponent<Tilemap>();
        if (tilemap == null)
        {
            Debug.LogWarning("Selected object does not have a Tilemap component.");
            return;
        }

        string mapName = go.name;
        ExportTilemap(tilemap, mapName, 32);
    }

    public static void ExportTilemap(Tilemap tilemap, string mapName, int chunkSize)
    {
        tilemap.CompressBounds();
        var b = tilemap.cellBounds;
        int minX = b.xMin;
        int minY = b.yMin;
        int maxX = b.xMax;
        int maxY = b.yMax;

        for (int cx = Mathf.FloorToInt((float)minX / chunkSize); cx <= Mathf.FloorToInt((float)(maxX-1) / chunkSize); cx++)
        {
            for (int cy = Mathf.FloorToInt((float)minY / chunkSize); cy <= Mathf.FloorToInt((float)(maxY-1) / chunkSize); cy++)
            {
                ExportChunk(tilemap, mapName, cx, cy, chunkSize);
            }
        }

        AssetDatabase.Refresh();
        Debug.Log("Export complete.");
    }

    private static void ExportChunk(Tilemap tilemap, string mapName, int cx, int cy, int chunkSize)
    {
        int ox = cx * chunkSize;
        int oy = cy * chunkSize;
        var chunk = new ChunkData();
        chunk.originX = ox;
        chunk.originY = oy;
        chunk.width = chunkSize;
        chunk.height = chunkSize;
        chunk.tiles = new int[chunkSize * chunkSize];
        chunk.blocking = new byte[chunkSize * chunkSize];

        var db = GetTileDatabase();
        if (db == null)
        {
            Debug.LogWarning("TileDatabase asset not found under Assets. Create one via Create->Map->TileDatabase and add tiles.");
            return;
        }

        for (int y = 0; y < chunkSize; y++)
        {
            for (int x = 0; x < chunkSize; x++)
            {
                int idx = y * chunkSize + x;
                var cell = new Vector3Int(ox + x, oy + y, 0);
                var tile = tilemap.GetTile(cell);
                int id = db.GetTileId(tile);
                chunk.tiles[idx] = id;

                // blocking if tilemap has collider for that tile; simple heuristic: tile != null on blocking layer
                chunk.blocking[idx] = (byte)(tile != null ? 1 : 0);
            }
        }

        string dir = "Assets/Resources/Maps";
        if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets/Resources", "Maps");
        string path = System.IO.Path.Combine(dir, $"{mapName}_{cx}_{cy}.json");
        string json = JsonUtility.ToJson(chunk);
        System.IO.File.WriteAllText(path, json);
        Debug.Log($"Exported chunk {cx},{cy} to {path}");
    }

    private static TileDatabase GetTileDatabase()
    {
        var guids = AssetDatabase.FindAssets("t:TileDatabase");
        if (guids == null || guids.Length == 0) return null;
        var path = AssetDatabase.GUIDToAssetPath(guids[0]);
        return AssetDatabase.LoadAssetAtPath<TileDatabase>(path);
    }
}

