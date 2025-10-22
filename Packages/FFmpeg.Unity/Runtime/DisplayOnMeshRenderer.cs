using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace FFmpeg.Unity
{
    [RequireComponent(typeof(MeshRenderer))]
    public class DisplayOnMeshRenderer : MonoBehaviour
    {
        public FFTexturePlayer texturePlayer;
        private MeshRenderer renderMesh;
        public int materialIndex = -1;
        public string textureProperty = "_EmissionMap";
        private MaterialPropertyBlock propertyBlock;
        public bool updateGIRealtime = true;

        private void Start()
        {
            if (!texturePlayer) texturePlayer = GetComponentInParent<FFTexturePlayer>(true);
            if (!texturePlayer)
            {
                Debug.LogWarning("No FFTexturePlayer found.");
            }
            propertyBlock = new MaterialPropertyBlock();
            renderMesh = GetComponent<MeshRenderer>();
            texturePlayer.OnDisplay += Display;
        }

        private void OnDestroy()
        {
            texturePlayer.OnDisplay -= Display;
        }

        public void Display(Texture2D texture)
        {
            if (texture != null) propertyBlock.SetTexture(textureProperty, texture);

            if (renderMesh != null)
            {
                if (materialIndex == -1) renderMesh.SetPropertyBlock(propertyBlock);
                else renderMesh.SetPropertyBlock(propertyBlock, materialIndex);
                if (updateGIRealtime) renderMesh.UpdateGIMaterials();
            }
        }
    }
}