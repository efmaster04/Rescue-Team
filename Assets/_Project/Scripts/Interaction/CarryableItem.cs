using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CompanySim
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class CarryableItem : InteractableBehaviour
    {
        public string uid, definitionId;
        [Range(0,100)] public float durability = 100;
        // Sert çarpma (fırlatma, düşme): çarpışma hızı ImpactDamageSpeed'i (m/sn) geçerse dayanıklılık ImpactDamage kadar düşer.
        // Tek ayar yeri burası (kullanıcı kararı: eşik ve miktar ayarlanabilir sabit).
        public const float ImpactDamageSpeed = 4f;
        public const float ImpactDamage = 4f;
        // Tanı: sert çarpma sayısı ve son çarpışma hızı (doğrulama okur)
        public int ImpactHits { get; private set; }
        public float LastImpactSpeed { get; private set; }
        // Fırlatıldı (henüz durmadı); durunca köprü WorldChanged ile kaydeder
        public bool Thrown { get; private set; }
        public bool Loaded { get; private set; }
        public int CargoSlot { get; private set; } = -1;
        Rigidbody body;
        RigidbodyInterpolation freeInterpolation;
        Collider[] colliders;
        BoxCollider footprint;
        Renderer[] visualRenderers;
        Canvas[] itemCanvases;
        bool[] originalCanvasEnabled;
        UnityEngine.Rendering.ShadowCastingMode[] originalShadows;
        public bool IsHandOccluded { get; private set; }
        public float NearestCameraDepth { get; private set; }
        CompanyCatalog catalog;
        AudioFeedback feedback;
        ManualModulePackage modulePackage;
        float lastImpact;
        public string DisplayName => modulePackage?modulePackage.DisplayName:catalog?catalog.Item(definitionId)?.displayName??definitionId:definitionId;
        public Vector3 HalfSize => footprint ? Vector3.Scale(footprint.size*.5f,transform.lossyScale) : Vector3.one*.2f;
        public Vector3 CenterOffset => footprint ? transform.TransformVector(footprint.center) : Vector3.zero;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            freeInterpolation=body.interpolation;
            colliders = GetComponentsInChildren<Collider>();
            footprint=GetComponent<BoxCollider>();
            visualRenderers=GetComponentsInChildren<Renderer>();
            originalShadows=visualRenderers.Select(x=>x.shadowCastingMode).ToArray();
            itemCanvases=GetComponentsInChildren<Canvas>(true);
            originalCanvasEnabled=itemCanvases.Select(c=>c.enabled).ToArray();
            catalog=FindAnyObjectByType<CompanyCatalog>();
            modulePackage=GetComponent<ManualModulePackage>();
            body.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;
            IgnorePlayerCollision();
        }
        void Start() { feedback=FindAnyObjectByType<AudioFeedback>(); }
        void OnCollisionEnter(Collision collision)
        {
            float speed=collision.relativeVelocity.magnitude;
            if(Loaded || body.isKinematic || speed<.9f || Time.time-lastImpact<.18f) return;
            lastImpact=Time.time; LastImpactSpeed=speed;
            if(feedback) feedback.Impact(transform.position,Mathf.Clamp01(speed/5));
            if(speed>=ImpactDamageSpeed)
            {
                // Sert çarpma: dayanıklılık düşer (fırlatılan ya da düşen eşya); kayıt ve HUD için dünya değişti
                durability=Mathf.Max(0,durability-ImpactDamage); ImpactHits++;
                var runtime=FindAnyObjectByType<CompanyRuntime>(); if(runtime) runtime.WorldChanged();
            }
        }
        void FixedUpdate()
        {
            if(!Thrown) return;
            if(body.isKinematic || Loaded) {Thrown=false; return;}
            if(body.linearVelocity.sqrMagnitude<.01f && body.angularVelocity.sqrMagnitude<.01f)
            {
                Thrown=false;
                var runtime=FindAnyObjectByType<CompanyRuntime>(); if(runtime) runtime.WorldChanged();
            }
        }
        public void CenterInHand()
        {
            if(footprint) transform.localPosition=-(transform.localRotation*Vector3.Scale(footprint.center,transform.localScale));
        }
        public void UpdateHandVisibility(Camera camera)
        {
            var forward=camera.transform.forward; var half=HalfSize;
            float depth=Mathf.Abs(Vector3.Dot(transform.right,forward))*half.x+
                Mathf.Abs(Vector3.Dot(transform.up,forward))*half.y+Mathf.Abs(Vector3.Dot(transform.forward,forward))*half.z;
            NearestCameraDepth=camera.transform.InverseTransformPoint(transform.position+CenterOffset).z-depth;
            SetHandOccluded(NearestCameraDepth<camera.nearClipPlane+.04f);
        }
        void SetHandOccluded(bool hidden)
        {
            if(IsHandOccluded==hidden) return;
            IsHandOccluded=hidden;
            for(int i=0;i<visualRenderers.Length;i++)
                if(visualRenderers[i]) visualRenderers[i].shadowCastingMode=hidden?UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly:originalShadows[i];
            for(int i=0;i<itemCanvases.Length;i++)
                if(itemCanvases[i]) itemCanvases[i].enabled=!hidden && originalCanvasEnabled[i];
        }
        public void IgnorePlayerCollision()
        {
            // Physics.IgnoreCollision is reset when an item collider is disabled.
            // Keep raycasts usable while letting players step through loose gear.
            foreach (var player in FindObjectsByType<PlayerInteractor>(FindObjectsSortMode.None))
            {
                var controller=player.GetComponent<CharacterController>();
                if (!controller) continue;
                foreach (var col in colliders)
                    if (col.enabled) Physics.IgnoreCollision(controller,col,true);
            }
        }
        public override string Prompt(PlayerInteractor player) => modulePackage?modulePackage.Prompt(player):player.HeldItem?"":"E — "+DisplayName+" al";
        public override void Interact(PlayerInteractor player)
        {
            if(modulePackage) {modulePackage.Interact(player); return;}
            if (!player.HeldItem && !Loaded) player.Hold(this);
        }
        public void AttachToHand(Transform hand)
        {
            SetHandOccluded(false);
            Loaded = false; CargoSlot = -1;
            body.interpolation=RigidbodyInterpolation.None;
            body.isKinematic = true;
            foreach (var col in colliders) col.enabled = false;
            transform.SetParent(hand, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            CenterInHand();
        }
        public void Drop(Vector3 position)
        {
            SetHandOccluded(false);
            Loaded = false; CargoSlot = -1;
            body.interpolation=RigidbodyInterpolation.None;
            transform.SetParent(null, true);
            transform.position = position;
            body.isKinematic = false;
            body.position=position; body.rotation=transform.rotation;
            body.linearVelocity=Vector3.zero; body.angularVelocity=Vector3.zero;
            body.interpolation=freeInterpolation;
            foreach (var col in colliders) col.enabled = true;
            IgnorePlayerCollision();
        }
        // Elden fırlat (bizim PlayerCombat sağ tık; CompanyPlayerBridge çağırır): Drop gibi serbest kalır, verilen hızla uçar.
        public void Throw(Vector3 position, Quaternion rotation, Vector3 velocity)
        {
            SetHandOccluded(false);
            Loaded = false; CargoSlot = -1;
            body.interpolation=RigidbodyInterpolation.None;
            transform.SetParent(null, true);
            transform.SetPositionAndRotation(position, rotation);
            body.isKinematic = false;
            body.position=position; body.rotation=rotation;
            body.interpolation=freeInterpolation;
            foreach (var col in colliders) col.enabled = true;
            IgnorePlayerCollision();
            body.linearVelocity=velocity; body.angularVelocity=UnityEngine.Random.insideUnitSphere*3f;
            Thrown=true; lastImpact=Time.time-.18f;
        }
        public void LoadInto(Transform socket, int slot)
        {
            SetHandOccluded(false);
            Loaded = true; CargoSlot = slot;
            body.interpolation=RigidbodyInterpolation.None;
            body.isKinematic = true;
            foreach (var col in colliders) col.enabled = false;
            transform.SetParent(socket, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
        }
        public void DockAt(Transform mount)
        {
            SetHandOccluded(false); Loaded=false; CargoSlot=-1; body.interpolation=RigidbodyInterpolation.None; body.isKinematic=true;
            transform.SetParent(mount,false); transform.localPosition=Vector3.zero; transform.localRotation=Quaternion.identity;
            foreach(var col in colliders) col.enabled=true; IgnorePlayerCollision();
        }
    }
}
