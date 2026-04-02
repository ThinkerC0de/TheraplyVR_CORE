using System;
using UnityEngine;

public class BreathingMist : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private PassiveMindfulnessController _passiveMindfulnessController;

    [SerializeField] private bool friend = false;
    [SerializeField] private Vector3 offset = new Vector3(0.0f, -0.1f, 0.0f);
    [SerializeField] private Vector3 rot = new Vector3(0.0f, 180.0f, 0.0f);
    private void Awake()
    {
        _passiveMindfulnessController = GetComponentInParent<PassiveMindfulnessController>();
        target = friend ? _passiveMindfulnessController.FriendHead: _passiveMindfulnessController.PlayerHead;
        transform.parent = target;
        transform.localPosition = offset;
        transform.localRotation = Quaternion.Euler(rot);
    }

    private void OnEnable()
    {
        // Vector3 newPos = target.position + target.forward * 0.3f + target.up * (- 0.2f);
        // transform.position = newPos;
    }

    private void FixedUpdate()
    {
        // Vector3 newPos = target.position + target.forward * 0.3f + target.up * (- 0.2f);
        // transform.position = newPos;
        // transform.rotation = target.rotation;
    }
}
