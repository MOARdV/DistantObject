using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace DistantObject
{
    // @ 1920x1080, 1 pixel with 60* FoV covers about 2 minutes of arc / 0.03 degrees
    class BodyFlare
    {
        public static double kerbinSMA = -1.0;
        public static double kerbinRadius;

        public CelestialBody body;
        public GameObject bodyMesh;
        public MeshRenderer meshRenderer;
        public Color color;
        public Vector3d cameraToBodyUnitVector;
        public double distanceFromCamera;
        public double sizeInDegrees;

        public double relativeRadiusSquared;
        public double bodyRadiusSquared;

        public void Update(Vector3d camPos, float camFOV)
        {
            // Update Body Flare
            Vector3d targetVectorToSun = FlightGlobals.Bodies[0].position - body.position;
            Vector3d targetVectorToCam = camPos - body.position;

            double targetSunRelAngle = Vector3d.Angle(targetVectorToSun, targetVectorToCam);

            cameraToBodyUnitVector = -targetVectorToCam.normalized;
            distanceFromCamera = targetVectorToCam.magnitude;

            double kerbinSMAOverBodyDist = kerbinSMA / targetVectorToSun.magnitude;
            double luminosity = kerbinSMAOverBodyDist * kerbinSMAOverBodyDist * relativeRadiusSquared;
            luminosity *= (0.5 + (32400.0 - targetSunRelAngle * targetSunRelAngle) / 64800.0);
            luminosity = (Math.Log10(luminosity) + 1.5) * (-2.0);

            // We need to clamp this value to remain < 5, since larger values cause a negative resizeVector.
            // This only appears to happen with some mod-generated worlds, but it's still a good practice
            // and not terribly expensive.
            float brightness = Math.Min(4.99f, (float)(luminosity + Math.Log10(distanceFromCamera / kerbinSMA)));

            //position, rotate, and scale mesh
            targetVectorToCam = (750000.0 * targetVectorToCam.normalized);
            bodyMesh.transform.position = camPos - targetVectorToCam;
            bodyMesh.transform.LookAt(camPos);

            float resizeFactor = (-750.0f * (brightness - 5.0f) * (0.7f + .99f * camFOV) / 70.0f) * DistantObjectSettings.DistantFlare.flareSize;
            bodyMesh.transform.localScale = new Vector3(resizeFactor, resizeFactor, resizeFactor);

            sizeInDegrees = Math.Acos(Math.Sqrt(distanceFromCamera * distanceFromCamera - bodyRadiusSquared) / distanceFromCamera) * Mathf.Rad2Deg;
        }

        ~BodyFlare()
        {
            //Debug.Log(Constants.DistantObject + string.Format(" -- BodyFlare {0} Destroy", (body != null) ? body.name : "(null bodyflare?)"));
        }
    }

    class VesselFlare
    {
        public Vessel referenceShip;
        public GameObject flareMesh;
        public MeshRenderer meshRenderer;
        public float luminosity;
        public float brightness;

        public void Update(Vector3d camPos, float camFOV)
        {
            Vector3d targetVectorToCam = camPos - referenceShip.transform.position;
            float targetDist = (float)Vector3d.Distance(referenceShip.transform.position, camPos);
            if (targetDist > 750000.0f && flareMesh.activeSelf)
            {
                flareMesh.SetActive(false);
            }
            if (targetDist < 750000.0f && !flareMesh.activeSelf)
            {
                flareMesh.SetActive(true);
            }

            if (flareMesh.activeSelf)
            {
                brightness = Mathf.Log10(luminosity) * (1.0f - Mathf.Pow(targetDist / 750000.0f, 1.25f));

                flareMesh.transform.position = camPos - targetDist * targetVectorToCam.normalized;
                flareMesh.transform.LookAt(camPos);
                float resizeFactor = (0.002f * targetDist * brightness * (0.7f + .99f * camFOV) / 70.0f) * DistantObjectSettings.DistantFlare.flareSize;

                flareMesh.transform.localScale = new Vector3(resizeFactor, resizeFactor, resizeFactor);
                //Debug.Log(string.Format("Resizing vessel flare {0} to {1} - brightness {2}, luminosity {3}", referenceShip.vesselName, resizeFactor, brightness, luminosity));
            }
        }

        ~VesselFlare()
        {
            // Why is this never called?
            //Debug.Log(Constants.DistantObject + string.Format(" -- VesselFlare {0} Destroy", (referenceShip != null) ? referenceShip.vesselName : "(null vessel?)"));
        }
    }

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class FlareDraw : MonoBehaviour
    {
        enum FlareType
        {
            Celestial,
            Vessel,
            Debris
        }

        private List<BodyFlare> bodyFlares = new List<BodyFlare>();
        private Dictionary<Vessel, VesselFlare> vesselFlares = new Dictionary<Vessel, VesselFlare>();

        private static float camFOV;
        private Vector3d camPos;
        private float atmosphereFactor = 1.0f;
        private float dimFactor = 1.0f;

        private static bool ExternalControl = false;

        private List<Vessel.Situations> situations = new List<Vessel.Situations>();

        private string showNameString = null;
        private Transform showNameTransform = null;
        private Color showNameColor;

        private List<Transform> scaledTransforms = new List<Transform>();
        private List<Vessel> deadVessels = new List<Vessel>();

        //--------------------------------------------------------------------
        // AddVesselFlare
        // Add a new vessel flare to our library
        private void AddVesselFlare(Vessel referenceShip)
        {
            // DistantObject/Flare/model has extents of (0.5, 0.5, 0.0), a 1/2 meter wide square.
            GameObject flare = GameDatabase.Instance.GetModel("DistantObject/Flare/model");
            GameObject flareMesh = Mesh.Instantiate(flare) as GameObject;
            Destroy(flareMesh.collider);
            DestroyObject(flare);

            flareMesh.name = referenceShip.vesselName;
            flareMesh.SetActive(true);

            MeshRenderer flareMR = flareMesh.GetComponentInChildren<MeshRenderer>();
            // MOARdV: valerian recommended moving vessel and body flares to
            // layer 10, but that behaves poorly for nearby / co-orbital objects.
            // Move vessels back to layer 0 until I can find a better place to
            // put it.
            // Renderer layers: http://wiki.kerbalspaceprogram.com/wiki/API:Layers
            flareMR.gameObject.layer = 15;
            flareMR.material.shader = Shader.Find("KSP/Alpha/Unlit Transparent");
            flareMR.material.color = Color.white;
            flareMR.castShadows = false;
            flareMR.receiveShadows = false;

            VesselFlare vesselFlare = new VesselFlare();
            vesselFlare.flareMesh = flareMesh;
            vesselFlare.meshRenderer = flareMR;
            vesselFlare.referenceShip = referenceShip;
            vesselFlare.luminosity = 5.0f + Mathf.Pow(referenceShip.GetTotalMass(), 1.25f);
            vesselFlare.brightness = 0.0f;

            vesselFlares.Add(referenceShip, vesselFlare);
        }

        //private void ListChildren(PSystemBody body, int idx)
        //{
        //    StringBuilder sb = new StringBuilder();
        //    for(int i=0; i< idx; ++i) sb.Append("  ");
        //    sb.Append("Body ");
        //    sb.Append(body.celestialBody.name);
        //    Debug.Log(sb.ToString());
        //    for(int i=0; i<body.children.Count; ++i)
        //    {
        //        ListChildren(body.children[i], idx + 1);
        //    }
        //}

        //--------------------------------------------------------------------
        // GenerateBodyFlares
        // Iterate over the celestial bodies and generate flares for each of
        // them.  Add the flare info to the dictionary.
        private void GenerateBodyFlares()
        {
            //--- HACK++
            //PSystemManager sm = PSystemManager.Instance;
            //Debug.Log("PSystemManager scaledSpaceFactor = " + sm.scaledSpaceFactor);
            //ListChildren(sm.systemPrefab.rootBody, 0);
            //--- HACK--

            //If Kerbin is parented to the Sun, set its SMA- otherwise iterate
            //through celestial bodies to locate which is parented to the Sun
            //and has Kerbin as a child. Set the highest parents SMA to kerbinSMA
            if(BodyFlare.kerbinSMA <= 0.0)
            {
                if(FlightGlobals.Bodies[1].referenceBody == FlightGlobals.Bodies[0])
                    BodyFlare.kerbinSMA = FlightGlobals.Bodies[1].orbit.semiMajorAxis;
                else
                {                    
                    foreach(CelestialBody current in FlightGlobals.Bodies)
                    {
                        if(current != FlightGlobals.Bodies[0])
                            if (current.referenceBody == FlightGlobals.Bodies[0] && current.HasChild(FlightGlobals.Bodies[1]))
                                BodyFlare.kerbinSMA = current.orbit.semiMajorAxis;
                    }
                }
                BodyFlare.kerbinRadius = FlightGlobals.Bodies[1].Radius;
            }
            bodyFlares.Clear();

            Dictionary<CelestialBody, Color> bodyColors = new Dictionary<CelestialBody, Color>();
            foreach (UrlDir.UrlConfig node in GameDatabase.Instance.GetConfigs("CelestialBodyColor"))
            {
                CelestialBody body = FlightGlobals.Bodies.Find(n => n.name == node.config.GetValue("name"));
                if (FlightGlobals.Bodies.Contains(body))
                {
                    Color color = ConfigNode.ParseColor(node.config.GetValue("color"));
                    color.r = 1.0f - (DistantObjectSettings.DistantFlare.flareSaturation * (1.0f - (color.r / 255.0f)));
                    color.g = 1.0f - (DistantObjectSettings.DistantFlare.flareSaturation * (1.0f - (color.g / 255.0f)));
                    color.b = 1.0f - (DistantObjectSettings.DistantFlare.flareSaturation * (1.0f - (color.b / 255.0f)));
                    color.a = 1;
                    if (!bodyColors.ContainsKey(body))
                    {
                        bodyColors.Add(body, color);
                    }
                }
            }

            GameObject flare = GameDatabase.Instance.GetModel("DistantObject/Flare/model");

            foreach (CelestialBody body in FlightGlobals.Bodies)
            {
                if (body != FlightGlobals.Bodies[0])
                {
                    BodyFlare bf = new BodyFlare();

                    GameObject flareMesh = Mesh.Instantiate(flare) as GameObject;
                    Destroy(flareMesh.collider);

                    flareMesh.name = body.bodyName;
                    flareMesh.SetActive(true);

                    MeshRenderer flareMR = flareMesh.GetComponentInChildren<MeshRenderer>();
                    // With KSP 1.0, putting these on layer 10 introduces 
                    // ghost flares that render for a while before fading away.
                    // These flares were moved to 10 because of an
                    // interaction with PlanetShine.  However, I don't see
                    // that problem any longer (where flares changed brightness
                    // during sunrise / sunset).  Valerian proposes instead using 15.
                    flareMR.gameObject.layer = 15;
                    flareMR.material.shader = Shader.Find("KSP/Alpha/Unlit Transparent");
                    if (bodyColors.ContainsKey(body))
                    {
                        flareMR.material.color = bodyColors[body];
                    }
                    else
                    {
                        flareMR.material.color = Color.white;
                    }
                    flareMR.castShadows = false;
                    flareMR.receiveShadows = false;

                    bf.body = body;
                    bf.bodyMesh = flareMesh;
                    bf.meshRenderer = flareMR;
                    bf.color = flareMR.material.color;
                    bf.relativeRadiusSquared = Math.Pow(body.Radius / FlightGlobals.Bodies[1].Radius, 2.0);
                    bf.bodyRadiusSquared = body.Radius * body.Radius;

                    bodyFlares.Add(bf);
                }
            }

            DestroyObject(flare);
        }

        //--------------------------------------------------------------------
        // GenerateVesselFlares
        // Iterate over the vessels, adding and removing flares as appropriate
        private void GenerateVesselFlares()
        {
            // See if there are vessels that need to be removed from our live
            // list
            foreach (Vessel v in vesselFlares.Keys)
            {
                if (v.loaded == true || !situations.Contains(v.situation))
                {
                    deadVessels.Add(v);
                }
            }

            for (int v=0; v<deadVessels.Count; ++v)
            {
                RemoveVesselFlare(deadVessels[v]);
            }
            deadVessels.Clear();

            // See which vessels we should add
            for (int i = 0; i < FlightGlobals.Vessels.Count; ++i )
            {
                Vessel vessel = FlightGlobals.Vessels[i];
                if (!vesselFlares.ContainsKey(vessel) && RenderableVesselType(vessel.vesselType) && !vessel.loaded && situations.Contains(vessel.situation))
                {
                    AddVesselFlare(vessel);
                }
            }
        }

        //--------------------------------------------------------------------
        // CheckDraw
        // Checks if the given mesh should be drawn.
        private void CheckDraw(GameObject flareMesh, MeshRenderer flareMR, Vector3d position, CelestialBody referenceBody, double objRadius, FlareType flareType)
        {
            Vector3d targetVectorToSun = FlightGlobals.Bodies[0].position - position;
            Vector3d targetVectorToRef = referenceBody.position - position;
            double targetRelAngle = Vector3d.Angle(targetVectorToSun, targetVectorToRef);
            double targetDist = Vector3d.Distance(position, camPos);
            double targetSize;
            if (flareType == FlareType.Celestial)
            {
                targetSize = objRadius;
            }
            else
            {
                targetSize = Math.Atan2(objRadius, targetDist) * Mathf.Rad2Deg;
            }
            double targetRefDist = Vector3d.Distance(position, referenceBody.position);
            double targetRefSize = Math.Acos(Math.Sqrt(Math.Pow(targetRefDist, 2.0) - Math.Pow(referenceBody.Radius, 2.0)) / targetRefDist) * Mathf.Rad2Deg;

            bool inShadow = false;
            if (referenceBody != FlightGlobals.Bodies[0] && targetRelAngle < targetRefSize)
            {
                inShadow = true;
            }

            bool isVisible;
            if (inShadow)
            {
                isVisible = false;
            }
            else
            {
                isVisible = true;

                for (int i = 0; i < bodyFlares.Count; ++i)
                {
                    if (bodyFlares[i].body.bodyName != flareMesh.name && bodyFlares[i].distanceFromCamera < targetDist && bodyFlares[i].sizeInDegrees > targetSize && Vector3d.Angle(bodyFlares[i].cameraToBodyUnitVector, position - camPos) < bodyFlares[i].sizeInDegrees)
                    {
                        isVisible = false;
                        break;
                    }
                }
            }

            Color color = flareMR.material.color;

            if (targetSize < (camFOV / 500.0f) && isVisible && !MapView.MapIsEnabled)
            {
                color.a = atmosphereFactor * dimFactor;
                if (targetSize > (camFOV / 1000.0f))
                {
                    color.a *= (float)(((camFOV / targetSize) / 500.0) - 1.0);
                }
                if (flareType == FlareType.Debris)
                {
                    color.a *= DistantObjectSettings.DistantFlare.debrisBrightness;
                }
                //color.a = 1.0f;
            }
            else
            {
                color.a = 0.0f;
            }

            flareMR.material.color = color;
        }

        //--------------------------------------------------------------------
        // RenderableVesselType
        // Indicates whether the specified vessel type is one we will render
        private bool RenderableVesselType(VesselType vesselType)
        {
            return !(vesselType == VesselType.Flag || vesselType == VesselType.EVA || (vesselType == VesselType.Debris && DistantObjectSettings.DistantFlare.ignoreDebrisFlare));
        }

        //--------------------------------------------------------------------
        // UpdateVar()
        // Update atmosphereFactor and dimFactor
        private void UpdateVar()
        {
            Vector3d sunBodyAngle = (FlightGlobals.Bodies[0].position - camPos).normalized;
            double sunBodyDist = FlightGlobals.Bodies[0].GetAltitude(camPos) + FlightGlobals.Bodies[0].Radius;
            double sunBodySize = Math.Acos(Math.Sqrt(Math.Pow(sunBodyDist, 2.0) - Math.Pow(FlightGlobals.Bodies[0].Radius, 2.0)) / sunBodyDist) * Mathf.Rad2Deg;

            atmosphereFactor = 1.0f;

            if (FlightGlobals.currentMainBody != null && FlightGlobals.currentMainBody.atmosphere)
            {
                double camAltitude = FlightGlobals.currentMainBody.GetAltitude(camPos);
                double atmAltitude = FlightGlobals.currentMainBody.atmosphereDepth;
                double atmCurrentBrightness = (Vector3d.Distance(camPos, FlightGlobals.Bodies[0].position) - Vector3d.Distance(FlightGlobals.currentMainBody.position, FlightGlobals.Bodies[0].position)) / (FlightGlobals.currentMainBody.Radius);

                if (camAltitude > (atmAltitude / 2.0) || atmCurrentBrightness > 0.15)
                {
                    atmosphereFactor = 1.0f;
                }
                else if (camAltitude < (atmAltitude / 10.0) && atmCurrentBrightness < 0.05)
                {
                    atmosphereFactor = 0.0f;
                }
                else
                {
                    if (camAltitude < (atmAltitude / 2.0) && camAltitude > (atmAltitude / 10.0) && atmCurrentBrightness < 0.15)
                    {
                        atmosphereFactor *= (float)((camAltitude - (atmAltitude / 10.0)) / (atmAltitude - (atmAltitude / 10.0)));
                    }
                    if (atmCurrentBrightness < 0.15 && atmCurrentBrightness > 0.05 && camAltitude < (atmAltitude / 2.0))
                    {
                        atmosphereFactor *= (float)((atmCurrentBrightness - 0.05) / (0.10));
                    }
                    if (atmosphereFactor > 1.0f)
                    {
                        atmosphereFactor = 1.0f;
                    }
                }
                // atmDensityASL isn't an exact match for atmosphereMultiplier from KSP 0.90, I think, but it
                // provides a '1' for Kerbin (1.2, actually)
                float atmThickness = (float)Math.Min(Math.Sqrt(FlightGlobals.currentMainBody.atmDensityASL), 1);
                atmosphereFactor = (atmThickness) * (atmosphereFactor) + (1.0f - atmThickness);
            }

            float sunDimFactor = 1.0f;
            float skyboxDimFactor;
            if(DistantObjectSettings.SkyboxBrightness.changeSkybox == true)
            {
                // Apply fudge factors here so people who turn off the skybox don't turn off the flares, too.
                // And avoid a divide-by-zero.
                skyboxDimFactor = Mathf.Max(0.5f, GalaxyCubeControl.Instance.maxGalaxyColor.r / Mathf.Max(0.0078125f, DistantObjectSettings.SkyboxBrightness.maxBrightness));
            }
            else
            {
                skyboxDimFactor = 1.0f;
            }

            // This code applies a fudge factor to flare dimming based on the
            // angle between the camera and the sun.  We need to do this because
            // KSP's sun dimming effect is not applied to maxGalaxyColor, so we
            // really don't know how much dimming is being done.
            float angCamToSun = Vector3.Angle(FlightCamera.fetch.mainCamera.transform.forward, sunBodyAngle);
            if (angCamToSun < (camFOV * 0.5f))
            {
                bool isVisible = true;
                for (int i = 0; i < bodyFlares.Count; ++i)
                {
                    if (bodyFlares[i].distanceFromCamera < sunBodyDist && bodyFlares[i].sizeInDegrees > sunBodySize && Vector3d.Angle(bodyFlares[i].cameraToBodyUnitVector, FlightGlobals.Bodies[0].position - camPos) < bodyFlares[i].sizeInDegrees)
                    {
                        isVisible = false;
                    }
                }
                if (isVisible)
                {
                    // Apply an arbitrary minimum value - the (x^4) function
                    // isn't right, but it does okay on its own.
                    float sunDimming = Mathf.Max(0.2f, Mathf.Pow(angCamToSun / (camFOV / 2.0f), 4.0f));
                    sunDimFactor *= sunDimming;
                }
            }
            dimFactor = DistantObjectSettings.DistantFlare.flareBrightness * Mathf.Min(skyboxDimFactor, sunDimFactor);
        }

        //--------------------------------------------------------------------
        // UpdateNameShown
        // Update the mousever name (if applicable)
        private void UpdateNameShown()
        {
            showNameTransform = null;
            if (DistantObjectSettings.DistantFlare.showNames && !MapView.MapIsEnabled)
            {
                Ray mouseRay = FlightCamera.fetch.mainCamera.ScreenPointToRay(Input.mousePosition);

                // Detect CelestialBody mouseovers
                double bestRadius = -1.0;
                foreach (BodyFlare bodyFlare in bodyFlares)
                {
                    if (bodyFlare.body == FlightGlobals.ActiveVessel.mainBody)
                    {
                        continue;
                    }

                    if (bodyFlare.meshRenderer.material.color.a > 0.0f)
                    {
                        Vector3d vectorToBody = bodyFlare.body.position - mouseRay.origin;
                        double mouseBodyAngle = Vector3d.Angle(vectorToBody, mouseRay.direction);
                        if (mouseBodyAngle < 1.0)
                        {
                            if (bodyFlare.body.Radius > bestRadius)
                            {
                                double distance = Vector3d.Distance(FlightCamera.fetch.mainCamera.transform.position, bodyFlare.body.position);
                                double angularSize = Mathf.Rad2Deg * bodyFlare.body.Radius / distance;
                                if (angularSize < 0.2)
                                {
                                    bestRadius = bodyFlare.body.Radius;
                                    showNameTransform = bodyFlare.body.transform;
                                    showNameString = bodyFlare.body.bodyName;
                                    showNameColor = bodyFlare.color;
                                }
                            }
                        }
                    }
                }

                if (showNameTransform == null)
                {
                    // Detect Vessel mouseovers
                    float bestBrightness = 0.01f; // min luminosity to show vessel name
                    foreach (VesselFlare vesselFlare in vesselFlares.Values)
                    {
                        if (vesselFlare.flareMesh.activeSelf && vesselFlare.meshRenderer.material.color.a > 0.0f)
                        {
                            Vector3d vectorToVessel = vesselFlare.referenceShip.transform.position - mouseRay.origin;
                            double mouseVesselAngle = Vector3d.Angle(vectorToVessel, mouseRay.direction);
                            if (mouseVesselAngle < 1.0)
                            {
                                float brightness = vesselFlare.brightness;
                                if (brightness > bestBrightness)
                                {
                                    bestBrightness = brightness;
                                    showNameTransform = vesselFlare.referenceShip.transform;
                                    showNameString = vesselFlare.referenceShip.vesselName;
                                    showNameColor = Color.white;
                                }
                            }
                        }
                    }
                }
            }
        }

        //--------------------------------------------------------------------
        // Awake()
        // Load configs, set up the callback, 
        private void Awake()
        {
            DistantObjectSettings.LoadConfig();

            Dictionary<string, Vessel.Situations> namedSituations = new Dictionary<string, Vessel.Situations> {
                { Vessel.Situations.LANDED.ToString(), Vessel.Situations.LANDED},
                { Vessel.Situations.SPLASHED.ToString(), Vessel.Situations.SPLASHED},
                { Vessel.Situations.PRELAUNCH.ToString(), Vessel.Situations.PRELAUNCH},
                { Vessel.Situations.FLYING.ToString(), Vessel.Situations.FLYING},
                { Vessel.Situations.SUB_ORBITAL.ToString(), Vessel.Situations.SUB_ORBITAL},
                { Vessel.Situations.ORBITING.ToString(), Vessel.Situations.ORBITING},
                { Vessel.Situations.ESCAPING.ToString(), Vessel.Situations.ESCAPING},
                { Vessel.Situations.DOCKED.ToString(), Vessel.Situations.DOCKED},
            };

            List<string> situationStrings = DistantObjectSettings.DistantFlare.situations.Split(',').ToList();

            foreach (string sit in situationStrings)
            {
                if (namedSituations.ContainsKey(sit))
                {
                    situations.Add(namedSituations[sit]);
                }
                else
                {
                    Debug.LogWarning(Constants.DistantObject + " -- Unable to find situation '" + sit + "' in my known situations atlas");
                }
            }

            if (DistantObjectSettings.DistantFlare.flaresEnabled)
            {
                Debug.Log(Constants.DistantObject + " -- FlareDraw enabled");
            }
            else
            {
                Debug.Log(Constants.DistantObject + " -- FlareDraw disabled");
            }

            GenerateBodyFlares();

            // Remove Vessels from our dictionaries just before they are destroyed.
            // After they are destroyed they are == null and this confuses Dictionary.
            GameEvents.onVesselWillDestroy.Add(RemoveVesselFlare);

            // Cache a list of the scaledTransforms so we know which worlds
            // are being rendered.
            scaledTransforms =
                ScaledSpace.Instance.scaledSpaceTransforms
                .Where(ss => ss.GetComponent<ScaledSpaceFader>() != null)
                .ToList();

            //--- HACK++
            //foreach(Transform sst in scaledTransforms)
            //{
            //    Debug.Log(string.Format("xform {0} @ {1}, which is {2}", sst.name, sst.position, ScaledSpace.ScaledToLocalSpace(sst.position)));
            //}
        }

        //--------------------------------------------------------------------
        // DestroyVesselFlare
        // Destroy the things associated with a VesselFlare
        private static void DestroyVesselFlare(VesselFlare v)
        {
            if (v.meshRenderer != null)
            {
                if (v.meshRenderer.material != null)
                {
                    Destroy(v.meshRenderer.material);
                }
                Destroy(v.meshRenderer);
            }
            if (v.flareMesh != null)
            {
                Destroy(v.flareMesh);
            }
        }

        //--------------------------------------------------------------------
        // OnDestroy()
        // Clean up after ourselves.
        private void OnDestroy()
        {
            GameEvents.onVesselWillDestroy.Remove(RemoveVesselFlare);
            foreach (VesselFlare v in vesselFlares.Values)
            {
                DestroyVesselFlare(v);
            }
            vesselFlares.Clear();

            foreach (BodyFlare b in bodyFlares)
            {
                if (b.meshRenderer != null)
                {
                    if (b.meshRenderer.material != null)
                    {
                        Destroy(b.meshRenderer.material);
                    }
                    Destroy(b.meshRenderer);
                }
                if (b.bodyMesh != null)
                {
                    Destroy(b.bodyMesh);
                }
            }
            bodyFlares.Clear();
        }

        //--------------------------------------------------------------------
        // RemoveVesselFlare
        // Removes a flare (either because a vessel was destroyed, or it's no
        // longer supposed to be part of the draw list).
        private void RemoveVesselFlare(Vessel v)
        {
            if (vesselFlares.ContainsKey(v))
            {
                DestroyVesselFlare(vesselFlares[v]);

                vesselFlares.Remove(v);
            }
        }

        //--------------------------------------------------------------------
        // FixedUpdate
        // Update visible vessel list
        public void FixedUpdate()
        {
            GenerateVesselFlares();
        }

        //--------------------------------------------------------------------
        // Update
        // Update flare positions and visibility
        private void Update()
        {
            showNameTransform = null;
            if (DistantObjectSettings.DistantFlare.flaresEnabled)
            {
                if (MapView.MapIsEnabled)
                {
                    // Big Hammer for map view - don't draw any flares
                    foreach (BodyFlare flare in bodyFlares)
                    {
                        flare.bodyMesh.SetActive(false);
                    }

                    foreach (VesselFlare vesselFlare in vesselFlares.Values)
                    {
                        vesselFlare.flareMesh.SetActive(false);
                    }
                }
                else
                {
                    camPos = FlightCamera.fetch.mainCamera.transform.position;

                    if (!ExternalControl)
                    {
                        camFOV = FlightCamera.fetch.mainCamera.fieldOfView;
                    }

                    if (DistantObjectSettings.debugMode)
                    {
                        Debug.Log(Constants.DistantObject + " -- Update");
                    }
                    foreach (BodyFlare flare in bodyFlares)
                    {
                        flare.Update(camPos, camFOV);
                        CheckDraw(flare.bodyMesh, flare.meshRenderer, flare.body.transform.position, flare.body.referenceBody, flare.sizeInDegrees, FlareType.Celestial);

                        if (flare.meshRenderer.material.color.a > 0.0f)
                        {
                            try
                            {
                                Renderer scaledRenderer = scaledTransforms.Find(x => x.name == flare.body.name).renderer;

                                //Transform t = scaledTransforms.Find(x => x.name == flare.body.name);
                                //Debug.Log(string.Format("xform {0} @ {1}, which is {2}; world is {3}", t.name, t.position, ScaledSpace.ScaledToLocalSpace(t.position), flare.body.transform.position));

                                flare.bodyMesh.SetActive(!(scaledRenderer.enabled && scaledRenderer.isVisible));
                            }
                            catch (Exception e)
                            {
                                flare.bodyMesh.SetActive(true);
                                Debug.LogException(e);
                            }
                        }
                        else
                        {
                            flare.bodyMesh.SetActive(false);
                        }
                    }

                    UpdateVar();

                    foreach (VesselFlare vesselFlare in vesselFlares.Values)
                    {
                        vesselFlare.Update(camPos, camFOV);

                        if (vesselFlare.flareMesh.activeSelf)
                        {
                            CheckDraw(vesselFlare.flareMesh, vesselFlare.meshRenderer, vesselFlare.flareMesh.transform.position, vesselFlare.referenceShip.mainBody, 5, (vesselFlare.referenceShip.vesselType == VesselType.Debris) ? FlareType.Debris : FlareType.Vessel);
                        }
                    }

                    UpdateNameShown();
                }
            }
        }

        //--------------------------------------------------------------------
        // OnGUI
        // Draws flare names when enabled
        private void OnGUI()
        {
            if (DistantObjectSettings.DistantFlare.flaresEnabled && DistantObjectSettings.DistantFlare.showNames && !MapView.MapIsEnabled && showNameTransform != null)
            {
                Vector3 screenPos = FlightCamera.fetch.mainCamera.WorldToScreenPoint(showNameTransform.position);
                Rect screenRect = new Rect(screenPos.x, Screen.height - screenPos.y - 20, 100, 20);
                GUIStyle s = new GUIStyle();
                s.normal.textColor = showNameColor;
                GUI.Label(screenRect, showNameString, s);
            }
        }

        //--------------------------------------------------------------------
        // SetFOV
        // Provides an external plugin the opportunity to set the FoV.
        public static void SetFOV(float FOV)
        {
            if (ExternalControl)
            {
                camFOV = FOV;
            }
        }

        //--------------------------------------------------------------------
        // SetExternalFOVControl
        // Used to indicate whether an external plugin wants to control the
        // field of view.
        public static void SetExternalFOVControl(bool Control)
        {
            ExternalControl = Control;
        }
    }
}
