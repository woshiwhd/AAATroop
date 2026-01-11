using UnityEngine;

namespace Script
{
    /// <summary>
    /// CameraFollow：简单的摄像机跟随脚本（可替代 Cinemachine 的基础用法）
    /// - 挂在 Camera 对象上
    /// - 设置 target 为 Player Transform
    /// - 支持平滑跟随和平滑边界（可根据需要扩展）
    /// </summary>
    public class CameraFollow : MonoBehaviour
    {
        public Transform target;
        public Vector3 offset = new Vector3(0, 0, -10);
        public float smoothTime = 0.12f;

        private Vector3 _velocity;

        void LateUpdate()
        {
            if (target == null) return;

            Vector3 targetPos = target.position + offset;
            transform.position = Vector3.SmoothDamp(transform.position, targetPos, ref _velocity, smoothTime);
        }
    }
}

