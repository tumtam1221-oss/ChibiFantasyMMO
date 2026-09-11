using ChibiFantasy.Client.World;
using ChibiFantasy.Core;
using UnityEditor;
using UnityEngine;

namespace ChibiFantasy.EditorTools
{
    /// <summary>
    /// Puts a monster on screen beside a player-sized reference, and plays its clips.
    /// </summary>
    /// <remarks>
    /// <b>Why a window and not a scene.</b> Harbor Town is production art and a validation
    /// prop dropped into it is a prop somebody eventually commits. This builds what it needs
    /// in a temporary scene, hands it back when the window closes, and leaves no asset
    /// behind -- so "let me look at the death animation" costs nothing and changes nothing.
    ///
    /// <b>Beside a reference of the player's real height.</b> The single question this gate
    /// has to answer visually is whether a Training Slime reads as a beginner monster, and
    /// that is not a number, it is a silhouette next to the thing it stands next to.
    ///
    /// <b>It plays clips, it does not drive the animator.</b> Sampling the clip directly is
    /// how a preview stays honest about what was authored: a state machine in between would
    /// let a transition hide a clip that is wrong.
    /// </remarks>
    public sealed class MonsterAnimationPreview : EditorWindow
    {
        private const string CataloguePath =
            "Assets/_Game/Prefabs/Presentation/MonsterVisualCatalogue.asset";

        /// <summary>The player's measured height, for the reference beside the monster.</summary>
        private const float PlayerHeight = 0.991f;

        private static readonly string[] ClipOrder =
        {
            "Slime_Idle", "Slime_Move", "Slime_Attack", "Slime_Hit", "Slime_Death",
        };

        private MonsterVisualCatalogue _catalogue;
        private string _monsterId = "monster.training_slime";

        private GameObject _stage;
        private GameObject _monster;
        private Animator _animator;
        private AnimationClip[] _clips = new AnimationClip[0];

        private int _clipIndex;
        private float _time;
        private bool _playing = true;
        private float _speed = 1f;
        private double _lastTick;

        [MenuItem("ChibiFantasy/Validation/Monster Animation Preview", priority = 300)]
        public static void Open()
        {
            GetWindow<MonsterAnimationPreview>("Monster Preview").Show();
        }

        private void OnEnable()
        {
            _catalogue = AssetDatabase.LoadAssetAtPath<MonsterVisualCatalogue>(CataloguePath);
            _lastTick = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Tick;
            Teardown();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Monster Animation Preview", EditorStyles.boldLabel);

            _catalogue = (MonsterVisualCatalogue)EditorGUILayout.ObjectField(
                "Catalogue", _catalogue, typeof(MonsterVisualCatalogue), false);

            _monsterId = EditorGUILayout.TextField("Monster id", _monsterId);

            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(_stage == null ? "Build stage" : "Rebuild stage"))
                {
                    Build();
                }

                using (new EditorGUI.DisabledScope(_stage == null))
                {
                    if (GUILayout.Button("Clear")) Teardown();
                }
            }

            if (_stage == null)
            {
                EditorGUILayout.HelpBox(
                    "Build the stage, then use the Scene view to look at the monster. "
                    + "The grey box beside it is the player's real height ("
                    + PlayerHeight.ToString("0.00") + " m).", MessageType.Info);
                return;
            }

            EditorGUILayout.Space();

            for (var i = 0; i < _clips.Length; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool on = i == _clipIndex;

                    if (GUILayout.Toggle(on, _clips[i].name, EditorStyles.miniButton) && !on)
                    {
                        _clipIndex = i;
                        _time = 0f;
                        _playing = true;
                    }

                    GUILayout.Label(_clips[i].length.ToString("0.00") + "s "
                        + (_clips[i].isLooping ? "loop" : "once"), GUILayout.Width(80f));
                }
            }

            EditorGUILayout.Space();

            _playing = EditorGUILayout.Toggle("Playing", _playing);
            _speed = EditorGUILayout.Slider("Speed", _speed, 0.05f, 2f);

            if (_clips.Length > 0)
            {
                AnimationClip clip = _clips[_clipIndex];

                _time = EditorGUILayout.Slider("Time", _time, 0f, Mathf.Max(clip.length, 0.001f));

                EditorGUILayout.LabelField("Normalised",
                    (clip.length <= 0f ? 0f : _time / clip.length).ToString("0.000"));
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Scrub Time to find a frame. For the attack, the impact pose is the one "
                + "where the slime is furthest forward.", MessageType.None);
        }

        private void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            float dt = (float)(now - _lastTick);
            _lastTick = now;

            if (_monster == null || _clips.Length == 0) return;

            AnimationClip clip = _clips[_clipIndex];

            if (_playing && clip.length > 0f)
            {
                _time += dt * _speed;

                if (_time > clip.length)
                {
                    // A looping clip wraps; a one-shot holds its last pose, which is what
                    // the game does with a death and what has to be looked at.
                    _time = clip.isLooping ? _time % clip.length : clip.length;
                }
            }

            clip.SampleAnimation(_monster, _time);

            Repaint();
        }

        private void Build()
        {
            Teardown();

            if (_catalogue == null)
            {
                _catalogue = AssetDatabase.LoadAssetAtPath<MonsterVisualCatalogue>(CataloguePath);
            }

            if (_catalogue == null)
            {
                EditorUtility.DisplayDialog("Monster Preview",
                    "No monster visual catalogue at " + CataloguePath, "OK");
                return;
            }

            var id = new DefinitionId(_monsterId);
            GameObject prefab = _catalogue.PrefabFor(id);

            if (prefab == null)
            {
                EditorUtility.DisplayDialog("Monster Preview",
                    "The catalogue has no model for " + _monsterId, "OK");
                return;
            }

            _stage = new GameObject("~MonsterPreviewStage");
            _stage.hideFlags = HideFlags.DontSave;

            _monster = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            _monster.transform.SetParent(_stage.transform, false);
            _monster.transform.localPosition = Vector3.zero;

            _animator = _monster.GetComponentInChildren<Animator>(true);

            // The animator is switched off: this window samples clips directly, and a
            // running state machine would fight it for the same transforms.
            if (_animator != null) _animator.enabled = false;

            BuildReference();

            _clips = ClipsFor(prefab);
            _clipIndex = 0;
            _time = 0f;
            _playing = true;

            Selection.activeGameObject = _monster;
            SceneView.lastActiveSceneView?.FrameSelected();
        }

        /// <summary>A block of the player's real height, to judge the monster against.</summary>
        private void BuildReference()
        {
            var reference = GameObject.CreatePrimitive(PrimitiveType.Cube);
            reference.name = "PlayerHeightReference";
            reference.hideFlags = HideFlags.DontSave;
            reference.transform.SetParent(_stage.transform, false);
            reference.transform.localScale = new Vector3(0.3f, PlayerHeight, 0.3f);

            // Sitting on the floor beside the monster, so both meet the ground at y = 0
            // and the comparison is of heights rather than of pivots.
            reference.transform.localPosition = new Vector3(0.55f, PlayerHeight * 0.5f, 0f);

            var collider = reference.GetComponent<Collider>();

            if (collider != null) DestroyImmediate(collider);
        }

        private static AnimationClip[] ClipsFor(GameObject prefab)
        {
            string path = AssetDatabase.GetAssetPath(prefab);

            // The prefab points at the FBX; the clips live in the FBX beside the mesh.
            var animator = prefab.GetComponentInChildren<Animator>(true);

            if (animator != null && animator.runtimeAnimatorController != null)
            {
                path = AssetDatabase.GetAssetPath(animator.runtimeAnimatorController);
            }

            var found = new System.Collections.Generic.List<AnimationClip>();

            foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip",
                new[] { System.IO.Path.GetDirectoryName(path).Replace('\\', '/') }))
            {
                string clipPath = AssetDatabase.GUIDToAssetPath(guid);

                foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(clipPath))
                {
                    var clip = o as AnimationClip;

                    if (clip == null || clip.name.StartsWith("__")) continue;

                    found.Add(clip);
                }
            }

            // Shown in the order the game uses them rather than alphabetically, so the list
            // reads as a monster's behaviour instead of as a folder.
            found.Sort((a, b) =>
            {
                int ia = System.Array.IndexOf(ClipOrder, a.name);
                int ib = System.Array.IndexOf(ClipOrder, b.name);

                if (ia < 0) ia = int.MaxValue;
                if (ib < 0) ib = int.MaxValue;

                return ia != ib ? ia.CompareTo(ib)
                    : string.Compare(a.name, b.name, System.StringComparison.Ordinal);
            });

            return found.ToArray();
        }

        private void Teardown()
        {
            if (_stage != null) DestroyImmediate(_stage);

            _stage = null;
            _monster = null;
            _animator = null;
            _clips = new AnimationClip[0];
        }
    }
}
