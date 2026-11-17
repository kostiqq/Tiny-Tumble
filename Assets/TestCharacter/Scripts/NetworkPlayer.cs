using System;
using StarterAssets;
using UnityEngine;

public class NetworkPlayer : MonoBehaviour
{
    [SerializeField] Rigidbody rigidBody3D;
    
    [SerializeField] private ConfigurableJoint mainJoint;
    
    
    [SerializeField] private StarterAssetsInputs  inputs;
    
    [SerializeField] float speedMultiply= 30;
    [SerializeField] float maxSpeed= 3;
    bool isGrounded = false;
    
    RaycastHit[] raycastHits = new RaycastHit[10];

    private SyncPhysicsObject[] syncPhysicsObjects;

    private void Awake()
    {
        syncPhysicsObjects = GetComponentsInChildren<SyncPhysicsObject>(); 
    }

    void FixedUpdate()
    {
       isGrounded = false;
       
       int numberOfHints = Physics.SphereCastNonAlloc(rigidBody3D.position, 0.1f, transform.up * -1, raycastHits, 0.5f);

       for (int i = 0; i < numberOfHints; i++)
       {
           if (raycastHits[i].transform.root == transform)
               continue;
           isGrounded = true;
           
           break;
       }
       
       if (!isGrounded)
           rigidBody3D.AddForce(Vector3.down * 10);
       
       float inputMagnitude =inputs.move.magnitude;

       if (inputMagnitude != 0)
       {
           Quaternion desiredDirection = Quaternion.LookRotation(new Vector3(inputs.move.x,0,inputs.move.y * -1), Vector3.up);
           
           mainJoint.targetRotation = Quaternion.RotateTowards(mainJoint.targetRotation, desiredDirection, Time.fixedDeltaTime * 300);
           
           Vector3 localVelocityVsForward = transform.forward * Vector3.Dot(transform.forward, rigidBody3D.linearVelocity);
           
           float localForwardVelocity = localVelocityVsForward.magnitude;

           if (localForwardVelocity < maxSpeed)
           {
               rigidBody3D.AddForce(transform.forward * inputMagnitude * speedMultiply);
           }
       }

       if (isGrounded && inputs.jump)
       {
           rigidBody3D.AddForce(Vector3.up * 20, ForceMode.Impulse);
           
           inputs.jump =  false;
       }

       for (int i = 0; i < syncPhysicsObjects.Length; i++)
       {
           syncPhysicsObjects[i].UpdateJointFromAnimation();
       }
    }
}
