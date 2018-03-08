﻿/*
 * Copyright(c) 2017-2018 Sketchfab Inc.
 * License: https://github.com/sketchfab/UnityGLTF/blob/master/LICENSE
 */

#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using SimpleJSON;
using UnityEngine.Networking;

namespace Sketchfab
{
    public enum REQUEST_TYPE
    {
        _categories,
        SEARCH,
        THUMBNAIL,
        ARCHIVE,
        MODEL_DATA
    }

    public enum SORT_BY
    {
        RELEVANCE,
        LIKES,
        VIEWS,
        RECENT,
    }

    public enum DATE
    {
        DEFAULT,
        DAY,
        MONTH,
        YEAR
    }

    public class SketchfabModel
    {
        // Model info
        public string uid;
        public string name;
        public string author;
        public string description = "";
        public int vertexCount = -1;
        public int faceCount = -1;
        public string hasAnimation = "";
        public string hasSkin = null;
        public JSONNode licenseJson;
        public float archiveSize;

        // Assets
        public Texture2D _thumbnail;
        public Texture2D _preview;
        public string previewUrl;

        public bool isFetched = false;

        public SketchfabModel(JSONNode node)
        {
            parseFromNode(node);
        }

        public SketchfabModel(JSONNode node, Texture2D thumbnail = null)
        {
            parseFromNode(node);
            if (thumbnail != null)
                _thumbnail = thumbnail;
        }

        private void parseFromNode(JSONNode node)
        {
            name = node["name"];
            description = richifyText(node["description"]);
            author = node["user"]["displayName"];
            uid = node["uid"];
            vertexCount = node["vertexCount"].AsInt;
            faceCount = node["faceCount"].AsInt;
        }

        private string richifyText(string text)
        {
            if(text != null)
            {
                text = text.Replace("<br>", "");
            }

            return text;
        }

        public void parseModelData(JSONNode node)
        {
            isFetched = true;

            hasAnimation = node["animationCount"].AsInt > 0 ? "Yes" : "No";
            licenseJson = node["license"].AsObject;
            //if (node["metadata"]["isRigged"])
            //hasSkin = node["metadata"]["isRigged"].AsBool;
        }
    }

    public class SketchfabBrowserManager
    {
        public static string ALL_CATEGORIES = "All";
        SketchfabImporter _importer;

        public SketchfabBrowserManager(UpdateCallback refresh=null, bool initialSearch=false)
        {
            checkValidity();
            _refreshCallback = refresh;

            if (initialSearch)
            {
                startInitialSearch();
            }
        }


        public void startInitialSearch()
        {
            _lastQuery = INITIAL_SEARCH;
            startSearch();
        }

        public void search(string query, bool staffpicked, bool animated, string categoryName, SORT_BY sortby, string maxFaceCount="")
        {
            reset();
            string searchQuery = START_QUERY;
            if(query.Length > 0)
            {
                searchQuery = searchQuery + "q=" + query;
            }

            if (maxFaceCount != "")
            {
                int fc = -1;
                if (int.TryParse(maxFaceCount, out fc))
                {
                    searchQuery = searchQuery + "&face_count=" + maxFaceCount;
                }
            }

            if (staffpicked)
            {
                searchQuery = searchQuery + "&staffpicked=true";
            }
            if (animated)
            {
                searchQuery = searchQuery + "&animated=true";
            }

            switch (sortby)
            {
                case SORT_BY.RECENT:
                    searchQuery = searchQuery + "&sort_by=" + "-publishedAt";
                    break;
                case SORT_BY.VIEWS:
                    searchQuery = searchQuery + "&sort_by=" + "-viewCount";
                    break;
                case SORT_BY.LIKES:
                    searchQuery = searchQuery + "&sort_by=" + "-likeCount";
                    break;
            }

            if (_categories[categoryName].Length > 0)
                searchQuery = searchQuery + "&categories=" + _categories[categoryName];

            _lastQuery = searchQuery;
            startSearch();
            _isFetching = true;
        }

        public void Refresh()
        {
            if (_refreshCallback != null)
                _refreshCallback();
        }

        // Maybe move this to a dedicated config script
        public Texture2D _defaultThumbnail;
        private const string INITIAL_SEARCH = "?type=models&downloadable=true&staffpicked=true&sort_by=-publishedAt";
        private const string START_QUERY = "?type=models&downloadable=true&";

        int _thumbnailSize = 128;
        float _previewRatio = 0.5625f;
        bool _hasFetchedPreviews = false;

        // _categories
        Dictionary<string, string> _categories;
        public SketchfabAPI _api;

        string _lastQuery;
        string _prevCursor = "";
        string _nextCursor = "";

        //Results
        List<string> _resultUids;
        Dictionary<string, SketchfabModel> _sketchfabModels;

        UpdateCallback _refreshCallback;
        RefreshCallback _downloadFinished;
        UnityGLTF.GLTFEditorImporter.ProgressCallback _importProgress;
        UnityGLTF.GLTFEditorImporter.RefreshWindow _importFinish;
        public bool _isFetching = false;

        byte[] _lastArchive;
        int _size;

        public void setRefreshCallback(UpdateCallback callback)
        {
            _refreshCallback = callback;
        }

        public void setImportProgressCallback(UnityGLTF.GLTFEditorImporter.ProgressCallback callback)
        {
            _importProgress = callback;
        }

        public void setImportFinishCallback(UnityGLTF.GLTFEditorImporter.RefreshWindow callback)
        {
            _importFinish = callback;
        }

        private void checkValidity()
        {
            SketchfabPlugin.checkValidity();
            if(_api == null)
            {
                _api = SketchfabPlugin.getAPI();
            }

            if (_resultUids == null)
            {
                _resultUids = new List<string>();
            }

            if (_sketchfabModels == null)
            {
                _sketchfabModels = new Dictionary<string, SketchfabModel>();
            }

            if (_categories == null)
            {
                fetchCategories();
            }

            if(_importer == null)
            {
                _importer = new SketchfabImporter(ImportProgress, ImportFinish);
            }
        }

        void ImportProgress(UnityGLTF.GLTFEditorImporter.IMPORT_STEP step, int current, int total)
        {
            if(_importProgress != null)
                _importProgress(step, current, total);
        }

        void ImportFinish()
        {
            if(_importFinish != null)
                _importFinish();
            _importer.cleanArtifacts();
        }

        void Awake()
        {
            checkValidity();
        }

        void OnEnable()
        {
            // Pre-fill model name with scene name if empty
            SketchfabPlugin.Initialize();
            checkValidity();
        }

        public void FinishUpdate()
        {
            EditorUtility.ClearProgressBar();
            if (_importFinish != null)
                _importFinish();
        }

        public void Update()
        {
            checkValidity();
            SketchfabPlugin.Update();
            _importer.Update();
        }

        // SEARCH
        void startNewSearch()
        {
            reset();
            startSearch();
        }

        public void fetchModelPreview()
        {
            if (!_hasFetchedPreviews)
            {
                foreach (SketchfabModel model in _sketchfabModels.Values)
                {
                    // Request model thumbnail
                    SketchfabRequest request = new SketchfabRequest(model.previewUrl);
                    request.setCallback(handleThumbnail);
                    _api.registerRequest(request);
                }
            }
            _hasFetchedPreviews = true;
        }

        void startSearch(string cursor = "")
        {
            _hasFetchedPreviews = false;
            SketchfabRequest request = new SketchfabRequest(SketchfabPlugin.Urls.searchEndpoint + _lastQuery + cursor);
            request.setCallback(handleSearch);
            _api.registerRequest(request);
        }

        void handleSearch(string response)
        {
            JSONNode json = Utils.JSONParse(response);
            JSONArray array = json["results"].AsArray;
            if (array == null)
                return;

            if (json["cursors"] != null)
            {
                if (json["cursors"]["next"].AsInt == 24)
                {
                    _prevCursor = "";
                }
                else if (_nextCursor != "null" && _nextCursor != "")
                {
                    _prevCursor = int.Parse(_nextCursor) - 24 + "";
                }

                _nextCursor = json["cursors"]["next"];
            }

            // First model fetch from uid
            foreach (JSONNode node in array)
            {
                _resultUids.Add(node["uid"]);
                if (!_sketchfabModels.ContainsKey(node["uid"]))
                {
                    // Add model to results
                    SketchfabModel model = new SketchfabModel(node, _defaultThumbnail);
                    model.previewUrl = getThumbnailUrl(node, 768);
                    _sketchfabModels.Add(node["uid"], model);

                    // Request model thumbnail
                    SketchfabRequest request = new SketchfabRequest(getThumbnailUrl(node));
                    request.setCallback(handleThumbnail);
                    _api.registerRequest(request);
                }
            }
            _isFetching = false;
            _refreshCallback();
        }

        void handleCategories(string result)
        {
            JSONArray _categoriesArray = Utils.JSONParse(result)["results"].AsArray;
            _categories.Clear();
            _categories.Add(ALL_CATEGORIES, "");
            foreach (JSONNode node in _categoriesArray)
            {
                _categories.Add(node["name"], node["slug"]);
            }
            _refreshCallback();
        }

        void handleModelData(string request)
        {
            JSONNode node = Utils.JSONParse(request);
            if (_sketchfabModels == null || !_sketchfabModels.ContainsKey(node["uid"]))
                return;

            _sketchfabModels[node["uid"]].parseModelData(node);
        }

        public void fetchModelInfo(string uid)
        {
            if(_sketchfabModels[uid].licenseJson == null)
            {
                SketchfabRequest request = new SketchfabRequest(SketchfabPlugin.Urls.modelEndPoint + "/" + uid);
                request.setCallback(handleModelData);
                _api.registerRequest(request);
            }
        }

        public void importArchive(byte[] data, string unzipDirectory, string importDirectory, string prefabName, bool addToCurrentScene=false)
        {
            if(!GLTFUtils.isFolderInProjectDirectory(importDirectory))
            {
                EditorUtility.DisplayDialog("Error", "Please select a path within your Asset directory", "OK");
                return;
            }

            _importer.configure(importDirectory, prefabName, addToCurrentScene);
            _importer.loadFromBuffer(data);
        }

        string extractUidFromUrl(string url)
        {
            string[] spl = url.Split('/');
            return spl[4];
        }

        void handleThumbnail(UnityWebRequest request)
        {
            string uid = extractUidFromUrl(request.url);
            if (!_sketchfabModels.ContainsKey(uid))
            {
                Debug.Log("Thumbnail request response dropped");
                return;
            }

            // Load thumbnail image
            byte[] data = request.downloadHandler.data;
			GL.sRGBWrite = true;
			Texture2D thumb = new Texture2D(2, 2);
            thumb.LoadImage(data);

            if (thumb.width >= 512)
            {
                var renderTexture = RenderTexture.GetTemporary(512, (int) (512 * _previewRatio), 24);
                var exportTexture = new Texture2D(thumb.height, thumb.height, TextureFormat.ARGB32, false);

                Graphics.Blit(thumb, renderTexture);
                exportTexture.ReadPixels(new Rect((thumb.width - thumb.height) / 2, 0, renderTexture.height, renderTexture.height), 0, 0);
                exportTexture.Apply();

                TextureScale.Bilinear(thumb, 512, (int)(512 * _previewRatio));
                _sketchfabModels[uid]._preview= thumb;
            }
            else
            {
                // Crop it to square
                var renderTexture = RenderTexture.GetTemporary(thumb.width, thumb.height, 24);
                var exportTexture = new Texture2D(thumb.height, thumb.height, TextureFormat.ARGB32, false);

                Graphics.Blit(thumb, renderTexture);
                exportTexture.ReadPixels(new Rect((thumb.width - thumb.height) / 2, 0, renderTexture.height, renderTexture.height), 0, 0);
                exportTexture.Apply();
                TextureScale.Bilinear(exportTexture, _thumbnailSize, _thumbnailSize);
                _sketchfabModels[uid]._thumbnail = exportTexture;
            }
        }

        void reset()
        {
            _resultUids.Clear();
            _sketchfabModels.Clear();
        }

        public List<string> getCategories()
        {
            List<string> categoryNames = new List<string>();
            foreach(string name in _categories.Keys)
            {
                categoryNames.Add(name);
            }

            return categoryNames;
        }

        public List<SketchfabModel> getResults()
        {
            List<SketchfabModel> _models = new List<SketchfabModel>();
            foreach(string uid in _resultUids)
            {
                _models.Add(_sketchfabModels[uid]);
            }

            return _models;
        }

        public SketchfabModel getModel(string uid)
        {
            if (!_sketchfabModels.ContainsKey(uid))
            {
                Debug.LogError("Model " + uid + " is not available");
                return null;
            }

            return _sketchfabModels[uid];
        }

        public bool hasNextResults()
        {
            return _nextCursor.Length > 0;
        }

        public void requestNextResults()
        {
            if(!hasNextResults())
            {
                Debug.LogError("No next results");
            }

            _resultUids.Clear();
            if (_sketchfabModels.Count > 0)
                _sketchfabModels.Clear();

            string cursorParam = "&cursor=" + _nextCursor;
            startSearch(cursorParam);
        }

        public bool hasPreviousResults()
        {
            return _prevCursor != "null" && _prevCursor.Length > 0;
        }
        public void requestPreviousResults()
        {
            if (!hasNextResults())
            {
                Debug.LogError("No next results");
            }

            _resultUids.Clear();
            if (_sketchfabModels.Count > 0)
                _sketchfabModels.Clear();

            string cursorParam = "&cursor=" + _prevCursor;
            startSearch(cursorParam);
        }

        void fetchCategories()
        {
            _categories = new Dictionary<string, string>();
            SketchfabRequest request = new SketchfabRequest(SketchfabPlugin.Urls.categoryEndpoint);
            request.setCallback(handleCategories);
            _api.registerRequest(request);
        }

        public bool canDisplayModels()
        {
            foreach(SketchfabModel model in _sketchfabModels.Values)
            {
                if(model._thumbnail == null)
                {
                    return false;
                }
            }

            return true;
        }

        private string getThumbnailUrl(JSONNode node, int maxWidth = 257)
        {
            JSONArray array = node["thumbnails"]["images"].AsArray;
            Dictionary<int, string> _thumbUrl = new Dictionary<int, string>();
            List<int> _intlist = new List<int>();
            foreach (JSONNode elt in array)
            {
                _thumbUrl.Add(elt["width"].AsInt, elt["url"]);
                _intlist.Add(elt["width"].AsInt);
            }

            _intlist.Sort();
            _intlist.Reverse();
            foreach (int res in _intlist)
            {
                if (res < maxWidth)
                    return _thumbUrl[res];
            }

            return null;
        }
    }
}



#endif