using UnityEngine;

public class SyncPhysicsObject : MonoBehaviour
{
    private Rigidbody rigidbody3D;
    private ConfigurableJoint joint;
    
    [SerializeField] Rigidbody animationRigidBody3D;
    
    [SerializeField] bool syncAnimation =  false;
    
    Quaternion startLocalRotation;

    void Awake()
    {
        rigidbody3D = GetComponent<Rigidbody>();
        joint = GetComponent<ConfigurableJoint>();
        
        startLocalRotation = transform.localRotation;
    }

    public void UpdateJointFromAnimation()
    {
        if (!syncAnimation)
            return;
        
        ConfigurableJointExtensions.SetTargetRotationLocal(joint, animationRigidBody3D.transform.localRotation, startLocalRotation);
    }
}
