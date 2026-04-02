using Autohand.Demo;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Autohand {

    public enum AxisEnum {
        right,
        up,
        forward,
        left,
        down,
        back
    }

    [System.Serializable]
    public struct HandPoseOffset {
        public OVRPlugin.BoneId jointID;
        public Vector3 localPositionOffset;
        public Vector3 localEularRotationOffset;
    }

    /// <summary>
    /// This version uses only OVRHand/OVRSkeleton for tracking.
    /// It updates a local “skeletonMap” using OVRSkeleton bones and then uses that to drive
    /// finger rotations and hand follow smoothing.
    /// </summary>
    public class MetaXRAutoHandTracking : MonoBehaviour {
        [Header("Hand Settings")]
        public Hand hand;
        public OVRHand ovrHand;
        public OVRSkeleton ovrSkeleton;
        public MetaXRHandControllerLink controllerLink;
        [Space]
        public AxisEnum upAxis = AxisEnum.up;
        public AxisEnum forwardAxis = AxisEnum.right;
        public Vector3 handOffset = Vector3.zero;
        public Vector3 handRotationOffset = Vector3.zero;
        public float handPoseSmoothingSpeed = 0.5f;
        public List<HandPoseOffset> handPoseOffsets = new List<HandPoseOffset>();

        [Header("Follow Settings")]
        public float followPositionSmoothing = 1f;
        public float followRotationSmoothing = 1f;

        [Header("Gizmos")]
        public bool drawGizmos = true;
        public Color gizmoColor = Color.white;

        [Header("Events")]
        public UnityHandEvent onControllerTrackingStart;
        public UnityHandEvent onControllerTrackingEnd;
        public UnityHandEvent onHandTrackingStart;
        public UnityHandEvent onHandTrackingEnd;
        


        // Pose data arrays used for smoothing finger movement.
        FingerPoseData[] _currentHandTrackingPose = new FingerPoseData[5];
        public FingerPoseData[] currentHandTrackingPose { get { return _currentHandTrackingPose; } }

        FingerPoseData[] _currentTargetPose = new FingerPoseData[5];
        public FingerPoseData[] currentTargetPose { get { return _currentTargetPose; } }

        public bool handTrackingActive { get; private set; }
        public bool controllerTrackingActive { get; private set; }

        // --- Joint Mapping (using OVRPlugin.BoneId values for consistency with Autohand) ---
        bool jointMapInitialized = false;
        OVRPlugin.BoneId[] _jointIDMap;
        OVRPlugin.BoneId[] jointIDMap {
            get {
                if(!jointMapInitialized) {
                    _jointIDMap = new OVRPlugin.BoneId[20];

                    // Index finger
                    _jointIDMap[(int)FingerEnum.index * 4 + (int)FingerJointEnum.knuckle] = OVRPlugin.BoneId.XRHand_IndexProximal;
                    _jointIDMap[(int)FingerEnum.index * 4 + (int)FingerJointEnum.middle]  = OVRPlugin.BoneId.XRHand_IndexIntermediate;
                    _jointIDMap[(int)FingerEnum.index * 4 + (int)FingerJointEnum.distal]  = OVRPlugin.BoneId.XRHand_IndexDistal;
                    _jointIDMap[(int)FingerEnum.index * 4 + (int)FingerJointEnum.tip]     = OVRPlugin.BoneId.XRHand_IndexTip;

                    // Middle finger
                    _jointIDMap[(int)FingerEnum.middle * 4 + (int)FingerJointEnum.knuckle] = OVRPlugin.BoneId.XRHand_MiddleProximal;
                    _jointIDMap[(int)FingerEnum.middle * 4 + (int)FingerJointEnum.middle]  = OVRPlugin.BoneId.XRHand_MiddleIntermediate;
                    _jointIDMap[(int)FingerEnum.middle * 4 + (int)FingerJointEnum.distal]  = OVRPlugin.BoneId.XRHand_MiddleDistal;
                    _jointIDMap[(int)FingerEnum.middle * 4 + (int)FingerJointEnum.tip]     = OVRPlugin.BoneId.XRHand_MiddleTip;

                    // Ring finger
                    _jointIDMap[(int)FingerEnum.ring * 4 + (int)FingerJointEnum.knuckle] = OVRPlugin.BoneId.XRHand_RingProximal;
                    _jointIDMap[(int)FingerEnum.ring * 4 + (int)FingerJointEnum.middle]  = OVRPlugin.BoneId.XRHand_RingIntermediate;
                    _jointIDMap[(int)FingerEnum.ring * 4 + (int)FingerJointEnum.distal]  = OVRPlugin.BoneId.XRHand_RingDistal;
                    _jointIDMap[(int)FingerEnum.ring * 4 + (int)FingerJointEnum.tip]     = OVRPlugin.BoneId.XRHand_RingTip;

                    // Pinky finger
                    _jointIDMap[(int)FingerEnum.pinky * 4 + (int)FingerJointEnum.knuckle] = OVRPlugin.BoneId.XRHand_LittleProximal;
                    _jointIDMap[(int)FingerEnum.pinky * 4 + (int)FingerJointEnum.middle]  = OVRPlugin.BoneId.XRHand_LittleIntermediate;
                    _jointIDMap[(int)FingerEnum.pinky * 4 + (int)FingerJointEnum.distal]  = OVRPlugin.BoneId.XRHand_LittleDistal;
                    _jointIDMap[(int)FingerEnum.pinky * 4 + (int)FingerJointEnum.tip]     = OVRPlugin.BoneId.XRHand_LittleTip;

                    // Thumb
                    _jointIDMap[(int)FingerEnum.thumb * 4 + (int)FingerJointEnum.knuckle] = OVRPlugin.BoneId.XRHand_ThumbMetacarpal;
                    _jointIDMap[(int)FingerEnum.thumb * 4 + (int)FingerJointEnum.middle]  = OVRPlugin.BoneId.XRHand_ThumbProximal;
                    _jointIDMap[(int)FingerEnum.thumb * 4 + (int)FingerJointEnum.distal]  = OVRPlugin.BoneId.XRHand_ThumbDistal;
                    _jointIDMap[(int)FingerEnum.thumb * 4 + (int)FingerJointEnum.tip]     = OVRPlugin.BoneId.XRHand_ThumbTip;

                    jointMapInitialized = true;
                }
                return _jointIDMap;
            }
        }

        // --- Skeleton Map ---
        bool handTrackingSkeletonInitialized = false;
        Dictionary<OVRPlugin.BoneId, Transform> _skeletonMap;
        Dictionary<OVRPlugin.BoneId, Transform> skeletonMap {
            get {
                if(!handTrackingSkeletonInitialized) {
                    _skeletonMap = new Dictionary<OVRPlugin.BoneId, Transform>();

                    // Create temporary GameObjects to drive our finger/skeleton logic.
                    var wrist = new GameObject("Wrist").transform;
                    var indexProximal = new GameObject("IndexProximal").transform;
                    var indexIntermediate = new GameObject("IndexIntermediate").transform;
                    var indexDistal = new GameObject("IndexDistal").transform;
                    var indexTip = new GameObject("IndexTip").transform;

                    var middleProximal = new GameObject("MiddleProximal").transform;
                    var middleIntermediate = new GameObject("MiddleIntermediate").transform;
                    var middleDistal = new GameObject("MiddleDistal").transform;
                    var middleTip = new GameObject("MiddleTip").transform;

                    var ringProximal = new GameObject("RingProximal").transform;
                    var ringIntermediate = new GameObject("RingIntermediate").transform;
                    var ringDistal = new GameObject("RingDistal").transform;
                    var ringTip = new GameObject("RingTip").transform;

                    var littleProximal = new GameObject("LittleProximal").transform;
                    var littleIntermediate = new GameObject("LittleIntermediate").transform;
                    var littleDistal = new GameObject("LittleDistal").transform;
                    var littleTip = new GameObject("LittleTip").transform;

                    var thumbMetacarpal = new GameObject("ThumbMetacarpal").transform;
                    var thumbProximal = new GameObject("ThumbProximal").transform;
                    var thumbDistal = new GameObject("ThumbDistal").transform;
                    var thumbTip = new GameObject("ThumbTip").transform;

                    // Setup hierarchy (each finger’s joints are parented correctly).
                    indexProximal.SetParent(wrist);
                    indexIntermediate.SetParent(indexProximal);
                    indexDistal.SetParent(indexIntermediate);
                    indexTip.SetParent(indexDistal);

                    middleProximal.SetParent(wrist);
                    middleIntermediate.SetParent(middleProximal);
                    middleDistal.SetParent(middleIntermediate);
                    middleTip.SetParent(middleDistal);

                    ringProximal.SetParent(wrist);
                    ringIntermediate.SetParent(ringProximal);
                    ringDistal.SetParent(ringIntermediate);
                    ringTip.SetParent(ringDistal);

                    littleProximal.SetParent(wrist);
                    littleIntermediate.SetParent(littleProximal);
                    littleDistal.SetParent(littleIntermediate);
                    littleTip.SetParent(littleDistal);

                    thumbMetacarpal.SetParent(wrist);
                    thumbProximal.SetParent(thumbMetacarpal);
                    thumbDistal.SetParent(thumbProximal);
                    thumbTip.SetParent(thumbDistal);

                    _skeletonMap.Add(OVRPlugin.BoneId.Hand_WristRoot, wrist);
                    _skeletonMap.Add(OVRPlugin.BoneId.XRHand_IndexProximal, indexProximal);
                    _skeletonMap.Add(OVRPlugin.BoneId.XRHand_IndexIntermediate, indexIntermediate);
                    _skeletonMap.Add(OVRPlugin.BoneId.XRHand_IndexDistal, indexDistal);
                    _skeletonMap.Add(OVRPlugin.BoneId.XRHand_IndexTip, indexTip);

                    _skeletonMap.Add(OVRPlugin.BoneId.XRHand_MiddleProximal, middleProximal);
                    _skeletonMap.Add(OVRPlugin.BoneId.XRHand_MiddleIntermediate, middleIntermediate);
                    _skeletonMap.Add(OVRPlugin.BoneId.XRHand_MiddleDistal, middleDistal);
                    _skeletonMap.Add(OVRPlugin.BoneId.XRHand_MiddleTip, middleTip);

                    _skeletonMap.Add(OVRPlugin.BoneId.XRHand_RingProximal, ringProximal);
                    _skeletonMap.Add(OVRPlugin.BoneId.XRHand_RingIntermediate, ringIntermediate);
                    _skeletonMap.Add(OVRPlugin.BoneId.XRHand_RingDistal, ringDistal);
                    _skeletonMap.Add(OVRPlugin.BoneId.XRHand_RingTip, ringTip);

                    _skeletonMap.Add(OVRPlugin.BoneId.XRHand_LittleProximal, littleProximal);
                    _skeletonMap.Add(OVRPlugin.BoneId.XRHand_LittleIntermediate, littleIntermediate);
                    _skeletonMap.Add(OVRPlugin.BoneId.XRHand_LittleDistal, littleDistal);
                    _skeletonMap.Add(OVRPlugin.BoneId.XRHand_LittleTip, littleTip);

                    _skeletonMap.Add(OVRPlugin.BoneId.XRHand_ThumbMetacarpal, thumbMetacarpal);
                    _skeletonMap.Add(OVRPlugin.BoneId.XRHand_ThumbProximal, thumbProximal);
                    _skeletonMap.Add(OVRPlugin.BoneId.XRHand_ThumbDistal, thumbDistal);
                    _skeletonMap.Add(OVRPlugin.BoneId.XRHand_ThumbTip, thumbTip);

                    handTrackingSkeletonInitialized = true;
                }
                return _skeletonMap;
            }
        }

        // --- Follow Transforms ---
        Transform handControllerFollow = null;

        Transform _handTrackingFollowOffset = null;
        public Transform handTrackingFollowOffset {
            get {
                if(_handTrackingFollowOffset == null) {
                    _handTrackingFollowOffset = new GameObject("HandFollowTrackingOffset").transform;
                    _handTrackingFollowOffset.parent = handTrackingFollow;
                    _handTrackingFollowOffset.localPosition = Vector3.zero;
                    _handTrackingFollowOffset.localRotation = Quaternion.identity;
                }
                return _handTrackingFollowOffset;
            }
        }

        Transform _handTrackingFollow = null;
        public Transform handTrackingFollow {
            get {
                if(_handTrackingFollow == null) {
                    _handTrackingFollow = new GameObject("HandFollowTracking").transform;
                    _handTrackingFollow.parent = hand.transform.parent;
                    _handTrackingFollow.localPosition = Vector3.zero;
                    _handTrackingFollow.localRotation = Quaternion.identity;
                    if(handControllerFollow == null) {
                        if(hand.follow != null)
                            handControllerFollow = hand.follow;
                        else
                            handControllerFollow = handTrackingFollowOffset;
                    }
                    hand.follow = handTrackingFollowOffset;
                }
                return _handTrackingFollow;
            }
        }

        // --- Helper Methods for Joint & Finger Data ---
        public OVRPlugin.BoneId GetHandJointID(FingerEnum fingerType, FingerJointEnum fingerJoint)
            => jointIDMap[(int)fingerType * 4 + (int)fingerJoint];

        public Transform GetHandTransform(OVRPlugin.BoneId jointID) {
            switch(jointID) {
                // Index
                case OVRPlugin.BoneId.Hand_Index1: return GetFinger(FingerEnum.index).knuckleJoint;
                case OVRPlugin.BoneId.Hand_Index2: return GetFinger(FingerEnum.index).middleJoint;
                case OVRPlugin.BoneId.Hand_Index3: return GetFinger(FingerEnum.index).distalJoint;
                case OVRPlugin.BoneId.Hand_IndexTip: return GetFinger(FingerEnum.index).tip;

                // Middle
                case OVRPlugin.BoneId.Hand_Middle1: return GetFinger(FingerEnum.middle).knuckleJoint;
                case OVRPlugin.BoneId.Hand_Middle2: return GetFinger(FingerEnum.middle).middleJoint;
                case OVRPlugin.BoneId.Hand_Middle3: return GetFinger(FingerEnum.middle).distalJoint;
                case OVRPlugin.BoneId.Hand_MiddleTip: return GetFinger(FingerEnum.middle).tip;

                // Ring
                case OVRPlugin.BoneId.Hand_Ring1: return GetFinger(FingerEnum.ring).knuckleJoint;
                case OVRPlugin.BoneId.Hand_Ring2: return GetFinger(FingerEnum.ring).middleJoint;
                case OVRPlugin.BoneId.Hand_Ring3: return GetFinger(FingerEnum.ring).distalJoint;
                case OVRPlugin.BoneId.Hand_RingTip: return GetFinger(FingerEnum.ring).tip;

                // Pinky
                case OVRPlugin.BoneId.Hand_Pinky1: return GetFinger(FingerEnum.pinky).knuckleJoint;
                case OVRPlugin.BoneId.Hand_Pinky2: return GetFinger(FingerEnum.pinky).middleJoint;
                case OVRPlugin.BoneId.Hand_Pinky3: return GetFinger(FingerEnum.pinky).distalJoint;
                case OVRPlugin.BoneId.Hand_PinkyTip: return GetFinger(FingerEnum.pinky).tip;

                // Thumb (skip Thumb0 in the finger joint mapping)
                case OVRPlugin.BoneId.Hand_Thumb1: return GetFinger(FingerEnum.thumb).knuckleJoint;
                case OVRPlugin.BoneId.Hand_Thumb2: return GetFinger(FingerEnum.thumb).middleJoint;
                case OVRPlugin.BoneId.Hand_Thumb3: return GetFinger(FingerEnum.thumb).distalJoint;
                case OVRPlugin.BoneId.Hand_ThumbTip: return GetFinger(FingerEnum.thumb).tip;

                // Not mapped to an actual finger joint
                default:
                    return null;
            }
        }

        Finger GetFinger(FingerEnum fingerType) {
            for(int i = 0; i < hand.fingers.Length; i++) {
                if(hand.fingers[i].fingerType == fingerType)
                    return hand.fingers[i];
            }
            return null;
        }

        Dictionary<OVRPlugin.BoneId, Pose> handPoseOffsetDictionary;

        protected virtual void OnEnable() {
            // Ensure our follow target is used
            hand.follow = handTrackingFollowOffset;

            for(int i = 0; i < _currentHandTrackingPose.Length; i++)
                _currentHandTrackingPose[i] = new FingerPoseData(hand, hand.fingers[i]);

            for(int i = 0; i < _currentTargetPose.Length; i++)
                _currentTargetPose[i] = new FingerPoseData(hand, hand.fingers[i]);

            if(controllerLink == null) {
                if(!hand.CanGetComponent(out controllerLink))
                    controllerLink = hand.gameObject.GetComponentInChildren<MetaXRHandControllerLink>();
            }

            // Build our hand pose offset dictionary.
            handPoseOffsetDictionary = new Dictionary<OVRPlugin.BoneId, Pose>();
            foreach(var poseOffset in handPoseOffsets) {
                var jointTransform = GetHandTransform(poseOffset.jointID);
                if(jointTransform != null) {
                    var basePos = jointTransform.localPosition;
                    var offset = new Pose(
                        basePos + poseOffset.localPositionOffset,
                        Quaternion.Euler(poseOffset.localEularRotationOffset)
                    );
                    handPoseOffsetDictionary.Add(poseOffset.jointID, offset);
                }
            }
        }

        protected virtual void OnDisable() {
            handPoseOffsetDictionary.Clear();
            hand.enableIK = true;
            hand.follow = handControllerFollow;
            if(controllerLink != null)
                controllerLink.enabled = true;
        }

        public bool IsControllerActive() {
            return OVRInput.IsControllerConnected(OVRInput.Controller.RTouch)
                   || OVRInput.IsControllerConnected(OVRInput.Controller.LTouch);
        }

        void OnControllersEnabled() {
            onControllerTrackingStart?.Invoke(hand);
            hand.enableIK = true;
            hand.follow = handControllerFollow;
        }

        void OnControllerDisabled() {
            onControllerTrackingEnd?.Invoke(hand);
        }

        void OnHandTrackingEnabled() {
            onHandTrackingStart?.Invoke(hand);
        }

        void OnHandTrackingDisabled() {
            onHandTrackingEnd?.Invoke(hand);
        }


        protected virtual void Update() {
            // Check if the hand is tracked via OVRHand
            bool isHandTracked = (ovrHand != null && ovrHand.IsTracked);

            bool isControllerActive = (controllerLink != null && IsControllerActive());

            if(isHandTracked) {
                if(isControllerActive && !handTrackingActive) {
                    OnControllerDisabled();
                    OnHandTrackingEnabled();
                }

                hand.enableIK = false; 
                hand.follow = handTrackingFollowOffset;

                if(controllerLink != null)
                    controllerLink.enabled = false;

                handTrackingActive = true;
                controllerTrackingActive = false;
            }
            else if(isControllerActive) {
                if(handTrackingActive && !controllerTrackingActive) {
                    OnControllersEnabled();
                    OnHandTrackingDisabled();
                }

                if(controllerLink != null)
                    controllerLink.enabled = true;

                handTrackingActive = false;
                controllerTrackingActive = true;
            }
            else {
                hand.enableIK = true;
            }

            // If we are successfully tracking the hand skeleton, update the skeleton
            if(ovrSkeleton != null && ovrSkeleton.IsDataValid && isHandTracked) {
                UpdateSkeletonTransform();
            }
        }


        /// <summary>
        /// Updates our local skeleton map with the latest OVRSkeleton bone transforms.
        /// Then it updates the follow target and each finger’s pose using smoothing.
        /// </summary>
        protected virtual void UpdateSkeletonTransform() {
            if(controllerTrackingActive || !handTrackingActive)
                return;

            var map = skeletonMap;

            if(ovrSkeleton != null && ovrSkeleton.IsDataValid) {
                foreach(var bone in ovrSkeleton.Bones) {
                    if(TryGetJointID(bone.Id, out OVRPlugin.BoneId jointID)) {
                        if(map.TryGetValue(jointID, out Transform targetTransform)) {
                            targetTransform.position = bone.Transform.position;
                            targetTransform.rotation = bone.Transform.rotation;
                        }
                    }
                }
            }

            // Update follow transform based on wrist data.
            var wristTransform = map[OVRPlugin.BoneId.Hand_WristRoot];
            float dist = Vector3.Distance(handTrackingFollow.position, wristTransform.position);
            float angleFrac = Quaternion.Angle(handTrackingFollow.rotation, wristTransform.rotation) / 180f;

            float movePos = dist * 60f * Time.deltaTime;
            movePos += 1f - (Time.deltaTime * 30f * followPositionSmoothing);

            float moveRot = angleFrac * 60f * Time.deltaTime;
            moveRot += 1f - (Time.deltaTime * 30f * followRotationSmoothing);

            handTrackingFollow.position = Vector3.Lerp(handTrackingFollow.position, wristTransform.position, movePos);
            handTrackingFollow.rotation = Quaternion.Lerp(handTrackingFollow.rotation, wristTransform.rotation, moveRot);
            handTrackingFollowOffset.localPosition = handOffset;
            handTrackingFollowOffset.localRotation = Quaternion.Euler(handRotationOffset);

            // Adjust wrist transform relative to the hand’s transform.
            wristTransform.position = hand.transform.TransformPoint(-handOffset);
            wristTransform.rotation = hand.transform.rotation
                * Quaternion.Inverse(Quaternion.Euler(handRotationOffset));

            // For each finger, update joint rotations toward the target given by our skeletonMap.
            foreach(var finger in hand.fingers) {
                int fingerIndex = (int)finger.fingerType;
                // If grabbing or holding, store the "target pose" from the start
                if(hand.IsGrabbing() || hand.IsHolding())
                    _currentTargetPose[fingerIndex].SetPoseData(hand, finger);

                for(int i = 0; i < (int)FingerJointEnum.tip; i++) {
                    var jointID = GetHandJointID(finger.fingerType, (FingerJointEnum)i);
                    var skeletonJoint = map[jointID];
                    var fingerTransform = finger.FingerJoints[i];
                    Vector3 forward = GetTransformAxis(skeletonJoint, forwardAxis);
                    Vector3 up = GetTransformAxis(skeletonJoint, upAxis);
                    Quaternion targetRotation = Quaternion.LookRotation(forward, up);
                    float angleDiff = Quaternion.Angle(fingerTransform.rotation, targetRotation) / 180f;

                    float lerpPoint = angleDiff * 60f * Time.deltaTime;
                    lerpPoint += 1f - (Time.deltaTime * 30f * handPoseSmoothingSpeed);
                    fingerTransform.rotation = Quaternion.Lerp(fingerTransform.rotation, targetRotation, lerpPoint);

                    if(handPoseOffsetDictionary.TryGetValue(jointID, out Pose poseOffset)) {
                        fingerTransform.localPosition = poseOffset.position;
                        fingerTransform.localRotation *= poseOffset.rotation;
                    }
                }
                _currentHandTrackingPose[fingerIndex].SetPoseData(hand, finger);
                if(hand.IsGrabbing() || hand.IsHolding())
                    _currentTargetPose[fingerIndex].SetFingerPose(finger);
            }
        }

        public Vector3 GetTransformAxis(Transform t, AxisEnum axis) {
            switch(axis) {
                case AxisEnum.right: return t.right;
                case AxisEnum.up: return t.up;
                case AxisEnum.forward: return t.forward;
                case AxisEnum.down: return -t.up;
                case AxisEnum.left: return -t.right;
                case AxisEnum.back: return -t.forward;
            }
            return Vector3.zero;
        }

        protected virtual void OnDrawGizmos() {
            if(!Application.isPlaying) return;
            if(drawGizmos && skeletonMap != null) {
                Gizmos.color = gizmoColor;
                foreach(var bone in skeletonMap) {
                    if(bone.Key != OVRPlugin.BoneId.Hand_WristRoot && bone.Value.parent != null) {
                        Gizmos.DrawLine(bone.Value.position, bone.Value.parent.position);
                    }
                }
            }
        }

        /// <summary>
        /// Maps OVRSkeleton bone IDs to our OVRPlugin.BoneId values.
        /// Adjust these cases as needed if your OVR SDK version changes the bone names.
        /// </summary>
        private bool TryGetJointID(OVRSkeleton.BoneId boneId, out OVRPlugin.BoneId jointID) {
            switch(boneId) {
                case OVRSkeleton.BoneId.Hand_WristRoot:
                    jointID = OVRPlugin.BoneId.Hand_WristRoot;
                    return true;
                // Index finger
                case OVRSkeleton.BoneId.XRHand_IndexProximal:
                    jointID = OVRPlugin.BoneId.XRHand_IndexProximal;
                    return true;
                case OVRSkeleton.BoneId.XRHand_IndexIntermediate:
                    jointID = OVRPlugin.BoneId.XRHand_IndexIntermediate;
                    return true;
                case OVRSkeleton.BoneId.XRHand_IndexDistal:
                    jointID = OVRPlugin.BoneId.XRHand_IndexDistal;
                    return true;
                case OVRSkeleton.BoneId.XRHand_IndexTip:
                    jointID = OVRPlugin.BoneId.XRHand_IndexTip;
                    return true;

                // Middle finger
                case OVRSkeleton.BoneId.XRHand_MiddleProximal:
                    jointID = OVRPlugin.BoneId.XRHand_MiddleProximal;
                    return true;
                case OVRSkeleton.BoneId.XRHand_MiddleIntermediate:
                    jointID = OVRPlugin.BoneId.XRHand_MiddleIntermediate;
                    return true;
                case OVRSkeleton.BoneId.XRHand_MiddleDistal:
                    jointID = OVRPlugin.BoneId.XRHand_MiddleDistal;
                    return true;
                case OVRSkeleton.BoneId.XRHand_MiddleTip:
                    jointID = OVRPlugin.BoneId.XRHand_MiddleTip;
                    return true;

                // Ring finger
                case OVRSkeleton.BoneId.XRHand_RingProximal:
                    jointID = OVRPlugin.BoneId.XRHand_RingProximal;
                    return true;
                case OVRSkeleton.BoneId.XRHand_RingIntermediate:
                    jointID = OVRPlugin.BoneId.XRHand_RingIntermediate;
                    return true;
                case OVRSkeleton.BoneId.XRHand_RingDistal:
                    jointID = OVRPlugin.BoneId.XRHand_RingDistal;
                    return true;
                case OVRSkeleton.BoneId.XRHand_RingTip:
                    jointID = OVRPlugin.BoneId.XRHand_RingTip;
                    return true;

                // Pinky finger
                case OVRSkeleton.BoneId.XRHand_LittleProximal:
                    jointID = OVRPlugin.BoneId.XRHand_LittleProximal;
                    return true;
                case OVRSkeleton.BoneId.XRHand_LittleIntermediate:
                    jointID = OVRPlugin.BoneId.XRHand_LittleIntermediate;
                    return true;
                case OVRSkeleton.BoneId.XRHand_LittleDistal:
                    jointID = OVRPlugin.BoneId.XRHand_LittleDistal;
                    return true;
                case OVRSkeleton.BoneId.XRHand_LittleTip:
                    jointID = OVRPlugin.BoneId.XRHand_LittleTip;
                    return true;

                // Thumb
                case OVRSkeleton.BoneId.XRHand_ThumbMetacarpal:
                    jointID = OVRPlugin.BoneId.XRHand_ThumbMetacarpal;
                    return true;
                case OVRSkeleton.BoneId.XRHand_ThumbProximal:
                    jointID = OVRPlugin.BoneId.XRHand_ThumbProximal;
                    return true;
                case OVRSkeleton.BoneId.XRHand_ThumbDistal:
                    jointID = OVRPlugin.BoneId.XRHand_ThumbDistal;
                    return true;
                case OVRSkeleton.BoneId.XRHand_ThumbTip:
                    jointID = OVRPlugin.BoneId.XRHand_ThumbTip;
                    return true;

                default:
                    jointID = OVRPlugin.BoneId.Invalid;
                    return false;
            }
        }
    }
}
