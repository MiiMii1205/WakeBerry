using UnityEngine;
using UnityEngine.Serialization;
using Zorro.Core;

namespace WakeBerry.Behaviours;

public class OrbThatMakesYouAwake : MonoBehaviour
{

    public Transform orb;
    public float maxDistance;
    public float minHotPerSecond;
    public float maxHotPerSecond;
    public float minHotPerTick;
    public float maxHotPerTick;
    public ParticleSystem particle;
    public Animator anim;
    public float animSpeed = 1f;
    public ParticleSystem ambientParticles;
    public FakeItem fakeBerry;
    public void Start() => anim.speed = animSpeed;

    public void Tick()
    {
        if (RunSettings.GetValue(RunSettings.SETTINGTYPE.Hazard_Heat) != 0)
        {
            UpdateHot();
        }
    }

    public void LateUpdate()
    {
        if (fakeBerry && fakeBerry.gameObject.activeInHierarchy)
        {
            anim.speed = animSpeed;
            ambientParticles.gameObject.SetActive(true);
            fakeBerry.transform.localPosition = new Vector3(-0.013f, -0.22f, 0.008f);
            fakeBerry.transform.localEulerAngles = Vector3.zero;
        }
        else
        {
            anim.speed = 0.0f;
            ambientParticles.gameObject.SetActive(false);
        }
    }

    public void UpdateHot(bool debug = false)
    {
        if (enabled && fakeBerry && fakeBerry.gameObject.activeInHierarchy &&
            Character.localCharacter.UnityObjectExists() && Character.localCharacter.data.fullyConscious)
        {
            var to = Character.localCharacter.Center - orb.transform.position;
            if ((to.magnitude <= maxDistance) &&
                GeometryUtility.TestPlanesAABB(
                    GeometryUtility.CalculateFrustumPlanes(MainCamera.instance.cam),
                    new Bounds(this.orb.transform.position, Vector3.one * 0.5f)))
            {
                var angle = Vector3.Angle(-MainCamera.instance.cam.transform.forward, to);
                var heatStrength = Mathf.InverseLerp(maxDistance, 2f, to.magnitude);
                var a = Mathf.Lerp(10f, 110f, heatStrength);
                var b = Mathf.InverseLerp(a, (float) (a / 2.0), angle);
                var amount = Mathf.Lerp(minHotPerTick, maxHotPerTick, Mathf.Min(heatStrength, b));
                Character.localCharacter.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Hot, amount);
                Character.localCharacter.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Drowsy, -amount);
                particle.Play();
            }
        }
    }
}