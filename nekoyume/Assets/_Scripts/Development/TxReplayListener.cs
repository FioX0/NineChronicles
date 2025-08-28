using UnityEngine;

namespace Nekoyume.Development
{
    // Global Enter-key listener to open the Tx replay popup regardless of Cheat widget visibility.
    internal sealed class TxReplayListener : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("TxReplayListener");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<TxReplayListener>();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Return))
            {
                Cheat.OpenTxReplayInput();
            }
        }
    }
}
