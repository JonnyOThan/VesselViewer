﻿using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using KSP.UI.Screens;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection.Emit;

namespace VesselView
{
    public class VesselViewer
    {

        //centerised... center
        //bounding box for the whole vessel, essentially
        private Vector3 minVecG = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        private Vector3 maxVecG = new Vector3(float.MinValue, float.MinValue, float.MinValue);

        //time of last update
        private float lastUpdate = 0.0f;

        //queue of parts yet to be drawn this draw
        private Queue<Part> partQueue = new Queue<Part>();
        private Queue<ViewerConstants.RectColor> rectQueue = new Queue<ViewerConstants.RectColor>();

        private Matrix4x4 worldToScreen;
        private Matrix4x4 worldToScreenFlattened;

        //gradient of colors for stage display
        private Color[] stageGradient;
        //line material
        private readonly Material lineMaterial ;

        public ViewerSettings basicSettings;
        public CustomModeSettings customMode;

        private RenderTexture screenBuffer;

        //stage counters
        private int stagesLastTime = 0;
        private int stagesThisTimeMax = 0;

        private int lastFrameDrawn = 0;

        private static Mesh bakedMesh = new Mesh();

        private static List<VesselViewer> activeInstances = new List<VesselViewer>();

        private static readonly int TransparentFxLayer = UnityEngine.LayerMask.NameToLayer("TransparentFX");

        public VesselViewer()
        {
            Debug.Log("VesselViewer.cs, creating basicSettings");
            // TODO: it would probably be better if we had a shader that worked with GL.Color
            lineMaterial = new Material(Shader.Find("Unlit/Color"));
            basicSettings = new ViewerSettings();
            activeInstances.Add(this);
        }

        public void nilOffset(int width, int height) {
            basicSettings.scrOffX = width / 2;
            basicSettings.scrOffY = height / 2;
        }

        public void manuallyOffset(int offsetX, int offsetY) {
            basicSettings.scrOffX += offsetX;
            basicSettings.scrOffY += offsetY;
        }

        public void forceRedraw() {
            lastUpdate = Time.time - 1f;
        }

        private void readyTexture(RenderTexture outputTexture) 
        {
            if (screenBuffer == null) createTexture(outputTexture);
            else if (screenBuffer.height != outputTexture.height | screenBuffer.width != outputTexture.width) createTexture(outputTexture);
        }

        private void createTexture(RenderTexture outputTexture) 
        {
            screenBuffer = new RenderTexture(outputTexture.width, outputTexture.height, outputTexture.depth, outputTexture.format);
        }

        public void drawCall(RenderTexture screen) {
            readyTexture(screen);
            RenderTexture activeTexture = screen;
            screen = screenBuffer;
            //MonoBehaviour.print("VV draw call");
            //Latency mode to limit to one frame per second if FPS is affected
            //also because it happens to look exactly like those NASA screens :3
            int frameDiff = Time.frameCount - lastFrameDrawn;
            bool redraw = false;
            if (basicSettings.latency == (int)ViewerConstants.LATENCY.OFF) redraw = true;
            else if (basicSettings.latency == (int)ViewerConstants.LATENCY.LOW) 
            {
                if (frameDiff >= 3) redraw = true;
            }
            else if (basicSettings.latency == (int)ViewerConstants.LATENCY.MEDIUM)
            {
                if (frameDiff >= 10) redraw = true;
            }
            else if (basicSettings.latency == (int)ViewerConstants.LATENCY.HIGH)
            {
                if (frameDiff >= 30) redraw = true;
            }
            else if (basicSettings.latency == (int)ViewerConstants.LATENCY.TOOHIGH)
            {
                if (frameDiff >= 75) redraw = true;
            }
            if (redraw) 
            {
                //if (ViewerConstants.VVDEBUG) MonoBehaviour.print("Redrawing");
                lastFrameDrawn = Time.frameCount;
                //MonoBehaviour.print("VV restarting draw, screen internal:"+internalScreen);
                restartDraw(screen);
                if (customMode == null){
                    if (basicSettings.autoCenter)
                    {
                        centerise(screen.width, screen.height);
                    }
                }
                else {
                    switch (customMode.CenteringOverride) 
                    {
                        case (int)CustomModeSettings.OVERRIDE_TYPES.AS_BASIC:
                            if (basicSettings.autoCenter)
                            {
                                centerise(screen.width, screen.height);
                            }break;
                        case (int)CustomModeSettings.OVERRIDE_TYPES.STATIC:
                            if (customMode.staticSettings.autoCenter)
                            {
                                centerise(screen.width, screen.height);
                            }break;
                        case (int)CustomModeSettings.OVERRIDE_TYPES.FUNCTION:
                            if (customMode.autoCenterDelegate(customMode))
                            {
                                centerise(screen.width, screen.height);
                            }break;  
                    }
                }

            }
            screen = activeTexture;
            Graphics.Blit(screenBuffer, screen);
            //MonoBehaviour.print("VV draw call done");
        }

        Vector3 GetSpinAngles()
        {
            Vector3 angles = Vector3.zero;
            float speed = 0;
            if (customMode == null)
            {
                speed = ViewerConstants.SPIN_SPEED_VAL[basicSettings.spinSpeed];
            }
            else
            {
                switch (customMode.OrientationOverride)
                {
                    case (int)CustomModeSettings.OVERRIDE_TYPES.AS_BASIC:
                        speed = ViewerConstants.SPIN_SPEED_VAL[basicSettings.spinSpeed];
                        break;
                    case (int)CustomModeSettings.OVERRIDE_TYPES.STATIC:
                        speed = ViewerConstants.SPIN_SPEED_VAL[customMode.staticSettings.spinSpeed];
                        break;
                    case (int)CustomModeSettings.OVERRIDE_TYPES.FUNCTION:
                        speed = customMode.spinSpeedDelegate(customMode);
                        break;
                }
            }

            int spinAxis = 0;
            if (customMode == null)
            {
                spinAxis = basicSettings.spinAxis;
            }
            else
            {
                switch (customMode.OrientationOverride)
                {
                    case (int)CustomModeSettings.OVERRIDE_TYPES.AS_BASIC:
                        spinAxis = basicSettings.spinAxis;
                        break;
                    case (int)CustomModeSettings.OVERRIDE_TYPES.STATIC:
                        spinAxis = customMode.staticSettings.spinAxis;
                        break;
                    case (int)CustomModeSettings.OVERRIDE_TYPES.FUNCTION:
                        spinAxis = customMode.spinAxisDelegate(customMode);
                        break;
                }
            }
            switch (spinAxis)
            {
                case (int)ViewerConstants.AXIS.X:
                    angles.x += ((Time.time * speed) % 360);
                    break;
                case (int)ViewerConstants.AXIS.Y:
                    angles.y += ((Time.time * speed) % 360);
                    break;
                case (int)ViewerConstants.AXIS.Z:
                    angles.z += ((Time.time * speed) % 360);
                    break;
            }

            return angles;
        }

        static readonly Matrix4x4 drawPlaneXZ = Matrix4x4.Rotate(Quaternion.Euler(0, 90, 0));
        static readonly Matrix4x4 drawPlaneYZ = Matrix4x4.Rotate(Quaternion.Euler(90, 0, 0));
        static readonly Matrix4x4 drawPlaneISO = Matrix4x4.Rotate(Quaternion.Euler(-15, 0, 0) * Quaternion.Euler(0, 30, 0));

        Matrix4x4 GetDrawPlaneMatrix()
		{
            int drawPlane = 0;
            if (customMode == null)
            {
                drawPlane = basicSettings.drawPlane;
            }
            else
            {
                switch (customMode.OrientationOverride)
                {
                    case (int)CustomModeSettings.OVERRIDE_TYPES.AS_BASIC:
                        drawPlane = basicSettings.drawPlane;
                        break;
                    case (int)CustomModeSettings.OVERRIDE_TYPES.STATIC:
                        drawPlane = customMode.staticSettings.drawPlane;
                        break;
                    case (int)CustomModeSettings.OVERRIDE_TYPES.FUNCTION:
                        drawPlane = customMode.drawPlaneDelegate(customMode);
                        break;
                }
            }
            switch (drawPlane)
            {
                case (int)ViewerConstants.PLANE.XZ:
                    return drawPlaneXZ;
                case (int)ViewerConstants.PLANE.YZ:
                    return drawPlaneYZ;
                case (int)ViewerConstants.PLANE.ISO:
                    return drawPlaneISO;
                case (int)ViewerConstants.PLANE.GRND:
                    Vessel vessel = FlightGlobals.ActiveVessel;
                    Quaternion invRotation = Quaternion.Inverse(vessel.srfRelRotation);
                    Quaternion groundRotation = Quaternion.FromToRotation(vessel.mainBody.GetSurfaceNVector(0, 0), vessel.mainBody.GetSurfaceNVector(vessel.latitude, vessel.longitude));
                    return Matrix4x4.Rotate(invRotation * groundRotation * Quaternion.Euler(0, 0, -90));
                case (int)ViewerConstants.PLANE.REAL:
                    return FlightGlobals.ActiveVessel.vesselTransform.localToWorldMatrix;
            }

            return Matrix4x4.identity;
        }

        void UpdateTransformMatrix()
        {
            Vector3 rotationAngles = GetSpinAngles();
            Matrix4x4 drawPlaneMatrix = GetDrawPlaneMatrix();
            worldToScreen = drawPlaneMatrix * Matrix4x4.Rotate(Quaternion.Euler(rotationAngles)) * FlightGlobals.ActiveVessel.transform.worldToLocalMatrix;

            worldToScreenFlattened = Matrix4x4.Scale(new Vector3(1, 1, 0.001f)) * worldToScreen;
        }

        /// <summary>
        /// Start a new draw cycle.
        /// </summary>
        private void restartDraw(RenderTexture screen)
        {
            //reset the vessel bounding box
            minVecG = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            maxVecG = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            lastUpdate = Time.time;
            partQueue.Clear();

            UpdateTransformMatrix();

            //FlightGlobals.ActiveVessel = FlightGlobals.ActiveVessel;
            if (!FlightGlobals.ActiveVessel.isEVA)
            {
                partQueue.Enqueue(FlightGlobals.ActiveVessel.rootPart);
            }
            try
            {
                renderToTexture(screen);
            }
            catch (Exception e) 
            {
                MonoBehaviour.print("Exception " + e + " during drawing");
            }
            GL.wireframe = false;
            partQueue.Clear();
        }

        /// <summary>
        /// render the vessel diagram to a texture.
        /// </summary>
        /// <param name="renderTexture">Texture to render to.</param>
        void renderToTexture(RenderTexture renderTexture)
        {
            //render not when invisible, grasshopper.
            if (basicSettings.screenVisible)
            {

                //switch rendering to the texture
                RenderTexture backupRenderTexture = RenderTexture.active;
                if (!renderTexture.IsCreated())
                    renderTexture.Create();
                renderTexture.DiscardContents();
                RenderTexture.active = renderTexture;

                //setup viewport and such
                GL.PushMatrix();
                GL.LoadPixelMatrix(0, renderTexture.width, 0, renderTexture.height);
                GL.Viewport(new Rect(0, 0, renderTexture.width, renderTexture.height));

                //clear the texture
                GL.Clear(true, true, Color.clear);
                
                //set up the screen position and scaling matrix
                Matrix4x4 matrix = Matrix4x4.TRS(new Vector3(basicSettings.scrOffX, basicSettings.scrOffY, 0), Quaternion.identity, new Vector3(basicSettings.scaleFact, basicSettings.scaleFact, 1));
                //dunno what this does, but I trust in the stolen codes
                lineMaterial.SetPass(0);
              
                while (partQueue.Count > 0)
                {
                    Part next = partQueue.Dequeue();
                    if (next != null)
                    {
                        renderPart(next, matrix);
                    }
                }
                GL.wireframe = true;
                //now render engine exhaust indicators
                if (customMode == null)
                {
                    if (basicSettings.displayEngines)
                    {
                        renderEngineThrusts(matrix);
                    }
                }
                else
                {
                    switch (customMode.MinimodesOverride)
                    {
                        case (int)CustomModeSettings.OVERRIDE_TYPES.AS_BASIC:
                            if (basicSettings.displayEngines)
                            {
                                renderEngineThrusts(matrix);    
                            } break;
                        case (int)CustomModeSettings.OVERRIDE_TYPES.STATIC:
                            if (customMode.staticSettings.displayEngines)
                            {
                                renderEngineThrusts(matrix);    
                            } break;
                        case (int)CustomModeSettings.OVERRIDE_TYPES.FUNCTION:
                            if (customMode.displayEnginesDelegate(customMode))
                            {
                                renderEngineThrusts(matrix); 
                            } break;
                    }
                }
                //now render the bounding boxes (so theyre on top)
                if (customMode == null)
                {
                    if (basicSettings.colorModeBox != (int)ViewerConstants.COLORMODE.HIDE)
                    {
                        renderRects(matrix);
                    }
                }
                else
                {
                    switch (customMode.ColorModeOverride)
                    {
                        case (int)CustomModeSettings.OVERRIDE_TYPES.AS_BASIC:
                            if (basicSettings.colorModeBox != (int)ViewerConstants.COLORMODE.HIDE)
                            {
                                renderRects(matrix);
                            }break;
                        case (int)CustomModeSettings.OVERRIDE_TYPES.STATIC:
                            if (customMode.staticSettings.colorModeBox != (int)ViewerConstants.COLORMODE.HIDE)
                            {
                                renderRects(matrix);
                            }break;
                        case (int)CustomModeSettings.OVERRIDE_TYPES.FUNCTION:
                            renderRects(matrix);
                            break;
                    }
                }
                
                //now render center of mass
                if (customMode == null)
                {
                    if (basicSettings.displayCOM) 
                    {
                        renderCOM(matrix);
                    }
                }
                else
                {
                    switch (customMode.MinimodesOverride)
                    {
                        case (int)CustomModeSettings.OVERRIDE_TYPES.AS_BASIC:
                            if (basicSettings.displayCOM) 
                            {
                                renderCOM(matrix);
                            } break;
                        case (int)CustomModeSettings.OVERRIDE_TYPES.STATIC:
                            if (customMode.staticSettings.displayCOM)
                            {
                                renderCOM(matrix);
                            } break;
                        case (int)CustomModeSettings.OVERRIDE_TYPES.FUNCTION:
                            if (customMode.displayCOMDelegate(customMode))
                            {
                                renderCOM(matrix);
                            } break;
                    }
                }
                //first, render the ground
                if (customMode == null)
                {
                    if (basicSettings.displayGround != (int)ViewerConstants.GROUND_DISPMODE.OFF)
                    {
                        renderGround(matrix);
                    }
                }
                else
                {
                    switch (customMode.MinimodesOverride)
                    {
                        case (int)CustomModeSettings.OVERRIDE_TYPES.AS_BASIC:
                            if (basicSettings.displayGround != (int)ViewerConstants.GROUND_DISPMODE.OFF)
                            {
                                renderGround(matrix);
                            } break;
                        case (int)CustomModeSettings.OVERRIDE_TYPES.STATIC:
                            if (customMode.staticSettings.displayGround != (int)ViewerConstants.GROUND_DISPMODE.OFF)
                            {
                                renderGround(matrix);
                            } break;
                        case (int)CustomModeSettings.OVERRIDE_TYPES.FUNCTION:
                            if (customMode.displayGroundDelegate(customMode) != (int)ViewerConstants.GROUND_DISPMODE.OFF)
                            {
                                renderGround(matrix);
                            } break;
                    }
                }
                //first, render the ground
                if (customMode == null)
                {
                    if (basicSettings.displayAxes)
                    {
                        renderAxes(matrix);
                    }
                }
                else
                {
                    switch (customMode.MinimodesOverride)
                    {
                        case (int)CustomModeSettings.OVERRIDE_TYPES.AS_BASIC:
                            if (basicSettings.displayAxes)
                            {
                                renderAxes(matrix);
                            } break;
                        case (int)CustomModeSettings.OVERRIDE_TYPES.STATIC:
                            if (customMode.staticSettings.displayAxes)
                            {
                                renderAxes(matrix);
                            } break;
                        case (int)CustomModeSettings.OVERRIDE_TYPES.FUNCTION:
                            if (customMode.displayAxesDelegate(customMode))
                            {
                                renderAxes(matrix);
                            } break;
                    }
                }
                
                /*if (settings.displayCOP)
                {
                    renderCOP(matrix);
                }*/
                //then set the max stages (for the stage coloring)
                stagesLastTime = stagesThisTimeMax;
                //undo stuff
                GL.wireframe = false;
                GL.PopMatrix();
                RenderTexture.active = backupRenderTexture;
            }
        }

        private void renderEngineThrusts(Matrix4x4 screenMatrix)
        {
            foreach (Part part in FlightGlobals.ActiveVessel.parts) 
            {
                string transformName = null;
                List<Propellant> propellants = null;
                float maxThrust = 0;
                float finalThrust = 0;
                bool operational = false;
                if (part.Modules.Contains("ModuleEngines"))
                {
                    ModuleEngines engineModule = (ModuleEngines)part.Modules["ModuleEngines"];
                    transformName = engineModule.thrustVectorTransformName;
                    propellants = engineModule.propellants;
                    maxThrust = engineModule.maxThrust;
                    finalThrust = engineModule.finalThrust;
                    operational = engineModule.isOperational;
                }
                else if (part.Modules.Contains("ModuleEnginesFX"))
                {
                    ModuleEnginesFX engineModule = (ModuleEnginesFX)part.Modules["ModuleEnginesFX"];
                    transformName = engineModule.thrustVectorTransformName;
                    propellants = engineModule.propellants;
                    maxThrust = engineModule.maxThrust;
                    finalThrust = engineModule.finalThrust;
                    operational = engineModule.isOperational;
                }
                
                if (transformName!=null) 
                {
                    //MonoBehaviour.print("Found an engine with a transform");
                    
                    float scale = 0;
                    scale = finalThrust / maxThrust;
                    bool Found_LiquidFuel = false;
                    bool Found_ElectricCharge = false;
                    bool Found_IntakeAir = false;
                    bool Found_XenonGas = false;
                    bool Found_Oxidizer = false;
                    bool Found_MonoPropellant = false;
                    bool Found_SolidFuel = false;
                    bool Deprived_LiquidFuel = false;
                    bool Deprived_ElectricCharge = false;
                    bool Deprived_IntakeAir = false;
                    bool Deprived_XenonGas = false;
                    bool Deprived_Oxidizer = false;
                    bool Deprived_MonoPropellant = false;
                    bool Deprived_SolidFuel = false;
                    //MonoBehaviour.print("Propellants for " + part.name);
                    foreach (Propellant propellant in propellants)
                    {
                        //MonoBehaviour.print(propellant.name);
                        if (propellant.name.Equals("LiquidFuel"))
                        {
                            Found_LiquidFuel = true;
                            if (propellant.isDeprived) Deprived_LiquidFuel = true;
                        }
                        else if (propellant.name.Equals("Oxidizer"))
                        {
                            Found_Oxidizer = true;
                            if (propellant.isDeprived) Deprived_Oxidizer = true;
                        }
                        else if (propellant.name.Equals("SolidFuel"))
                        {
                            Found_SolidFuel = true;
                            if (propellant.isDeprived) Deprived_SolidFuel = true;
                        }
                        else if (propellant.name.Equals("IntakeAir"))
                        {
                            Found_IntakeAir = true;
                            if (propellant.isDeprived) Deprived_IntakeAir = true;
                        }
                        else if (propellant.name.Equals("MonoPropellant"))
                        {
                            Found_MonoPropellant = true;
                            if (propellant.isDeprived) Deprived_MonoPropellant = true;
                        }
                        else if (propellant.name.Equals("XenonGas"))
                        {
                            Found_XenonGas = true;
                            if (propellant.isDeprived) Deprived_XenonGas = true;
                        }
                        else if (propellant.name.Equals("ElectricCharge"))
                        {
                            Found_ElectricCharge = true;
                            if (propellant.isDeprived) Deprived_ElectricCharge = true;
                        }
                    }

                    Matrix4x4 transMatrix = genTransMatrix(part.partTransform, true);
                    //if online, render exhaust
                    if (scale > 0.01f) 
                    {
                        if (!transformName.Equals(""))
                        {
                            Transform thrustTransform = part.FindModelTransform(transformName);
                            transMatrix = genTransMatrix(thrustTransform, true);
                            //default to magenta
                            Color color = Color.magenta;
                            //liquid fuel engines
                            if (Found_LiquidFuel & Found_Oxidizer) color = new Color(1, 0.5f, 0);
                            //SRBs
                            else if (Found_SolidFuel) color = new Color(1f, 0.1f, 0.1f);
                            //air breathing engines
                            else if (Found_LiquidFuel & Found_IntakeAir) color = new Color(0.9f, 0.7f, 0.8f);
                            //ion engines
                            else if (Found_XenonGas & Found_ElectricCharge) color = new Color(0f, 0.5f, 1f);
                            //monoprop engines
                            else if (Found_MonoPropellant) color = new Color(0.9f, 0.9f, 0.9f);
                            float massSqrt = (float)Math.Sqrt(part.mass);
                            scale *= massSqrt;
                            renderCone(thrustTransform, scale, massSqrt, screenMatrix, color);

                            Vector3 v = new Vector3(0, 0, scale + part.mass);
                            v = transMatrix.MultiplyPoint3x4(v);
                            if (v.x < minVecG.x) minVecG.x = v.x;
                            if (v.y < minVecG.y) minVecG.y = v.y;
                            if (v.z < minVecG.z) minVecG.z = v.z;
                            if (v.x > maxVecG.x) maxVecG.x = v.x;
                            if (v.y > maxVecG.y) maxVecG.y = v.y;
                            if (v.z > maxVecG.z) maxVecG.z = v.z;
                        }
                        
                    }
                    //render icon
                    float div = 6 / basicSettings.scaleFact;
                    Vector3 posStr = new Vector3();
                    posStr = transMatrix.MultiplyPoint3x4(posStr);
                    //out of fuel
                    if ((Found_LiquidFuel & Deprived_LiquidFuel) | (Found_SolidFuel & Deprived_SolidFuel) | (Found_MonoPropellant & Deprived_MonoPropellant) | (Found_XenonGas & Deprived_XenonGas) | (Found_Oxidizer & Deprived_Oxidizer))
                        renderIcon(new Rect(-div + posStr.x, -div + posStr.y, 2 * div, 2 * div), screenMatrix, Color.red, (int)ViewerConstants.ICONS.ENGINE_NOFUEL);
                    else if ((Found_ElectricCharge & Deprived_ElectricCharge))
                        renderIcon(new Rect(-div + posStr.x, -div + posStr.y, 2 * div, 2 * div), screenMatrix, Color.cyan, (int)ViewerConstants.ICONS.ENGINE_NOPOWER);
                    else if ((Found_IntakeAir & Deprived_IntakeAir))
                        renderIcon(new Rect(-div + posStr.x, -div + posStr.y, 2 * div, 2 * div), screenMatrix, Color.cyan, (int)ViewerConstants.ICONS.ENGINE_NOAIR);
                    else if (scale >= 0.01f)
                        renderIcon(new Rect(-div + posStr.x, -div + posStr.y, 2 * div, 2 * div), screenMatrix, new Color(1,0.5f,0), (int)ViewerConstants.ICONS.ENGINE_ACTIVE);
                    else 
                        {
                            if (!operational)
                                renderIcon(new Rect(-div + posStr.x, -div + posStr.y, 2 * div, 2 * div), screenMatrix, Color.yellow, (int)ViewerConstants.ICONS.ENGINE_INACTIVE);
                            else
                                renderIcon(new Rect(-div + posStr.x, -div + posStr.y, 2 * div, 2 * div), screenMatrix, Color.green, (int)ViewerConstants.ICONS.ENGINE_READY);
                        }

                    
                    //renderIcon(new Rect(-div + posEnd.x, -div + posEnd.y, 2 * div, 2 * div), screenMatrix, Color.yellow, (int)ViewerConstants.ICONS.SQUARE_DIAMOND);
                }
            }
        }



        /// <summary>
        /// Renders the part bounding boxes
        /// </summary>
        /// <param name="screenMatrix">Screen transformation matrix</param>
        void renderRects(Matrix4x4 screenMatrix)
        {
            //render them! render them all upon m-wait.
            while (rectQueue.Count > 0)
            {
                ViewerConstants.RectColor next = rectQueue.Dequeue();
                //this way invisible squares dont cover up visible ones
                if(next.color.a != 0) renderRect(next.rect, screenMatrix, next.color);
            }

        }

        private void renderGround(Matrix4x4 screenMatrix)
        {
            
            //Vector3 groundN = FlightGlobals.ActiveVessel.mainBody.GetRelSurfaceNVector(FlightGlobals.ActiveVessel.latitude, FlightGlobals.ActiveVessel.longitude);
            Vector3d position = FlightGlobals.ActiveVessel.vesselTransform.position;
            //unit vectors in the up (normal to planet surface), east, and north (parallel to planet surface) directions
            //Vector3d eastUnit = FlightGlobals.ActiveVessel.mainBody.getRFrmVel(position).normalized; //uses the rotation of the body's frame to determine "east"
            Vector3d upUnit = (position - FlightGlobals.ActiveVessel.mainBody.position).normalized;
            Vector3 groundDir = position + upUnit;
            //Quaternion lookAt = Quaternion.LookRotation(upUnit).Inverse();
            //MonoBehaviour.print("upUnit "+upUnit);
            Matrix4x4 worldToLocal = FlightGlobals.ActiveVessel.vesselTransform.worldToLocalMatrix;
            Vector3 localSpaceNormal = worldToLocal.MultiplyPoint3x4(groundDir);
            Vector3 perp1;
            if (localSpaceNormal.y > 0.9 | localSpaceNormal.y < -0.9)
                perp1 = Vector3.Cross(Vector3.right, localSpaceNormal);
            else
                perp1 = Vector3.Cross(Vector3.up, localSpaceNormal);
            perp1 = perp1.normalized;
            Vector3 perp2 = Vector3.Cross(localSpaceNormal+perp1, localSpaceNormal);
            perp2 = perp2.normalized;
            //MonoBehaviour.print("localSpaceNormal " + localSpaceNormal);
            //MonoBehaviour.print("perp1 " + perp1);
            //MonoBehaviour.print("perp2 " + perp2);
            //Vector3 worldSpaceNormal = FlightGlobals.ActiveVessel.vesselTransform.localToWorldMatrix.MultiplyPoint3x4(groundDir);
            double altitude = FlightGlobals.ActiveVessel.altitude - FlightGlobals.ActiveVessel.terrainAltitude;
            if (altitude > ViewerConstants.MAX_ALTITUDE) return;
            float biggestCrossSection = maxVecG.x - minVecG.x;
            if (maxVecG.y - minVecG.y > biggestCrossSection) biggestCrossSection = maxVecG.y - minVecG.y;
            if (maxVecG.z - minVecG.z > biggestCrossSection) biggestCrossSection = maxVecG.z - minVecG.z;
            //smallestCrossSection = smallestCrossSection / settings.scaleFact;
            //MonoBehaviour.print("biggestCrossSection " + biggestCrossSection);
            //biggestCrossSection = 20;
            Vector3 groundBelow = localSpaceNormal * -(float)altitude;
            Vector3 groundBelow1 = groundBelow + (perp1 * biggestCrossSection);
            Vector3 groundBelow2 = groundBelow - (perp1 * biggestCrossSection);
            Vector3 groundBelow3 = groundBelow + (perp2 * biggestCrossSection);
            Vector3 groundBelow4 = groundBelow - (perp2 * biggestCrossSection);
            /*Vector3 groundBelow = localSpaceNormal * 10;
            MonoBehaviour.print("localSpaceNormal " + localSpaceNormal);
            groundBelow = worldSpaceNormal * 10;
            MonoBehaviour.print("worldSpaceNormal " + localSpaceNormal);
            groundBelow = localSpaceNormal * 10;*/
            //Vector3 groundBelow = new Vector3(0, -(float)altitude, 0);
            /*Vector3 groundBelow1 = new Vector3(biggestCrossSection, -(float)altitude, biggestCrossSection);
            Vector3 groundBelow2 = new Vector3(biggestCrossSection, -(float)altitude, -biggestCrossSection);
            Vector3 groundBelow3 = new Vector3(-biggestCrossSection, -(float)altitude, -biggestCrossSection);
            Vector3 groundBelow4 = new Vector3(-biggestCrossSection, -(float)altitude, biggestCrossSection);*/
            //Vector3 direction = groundBelow + groundN;
            //MonoBehaviour.print("COM>"+COM);
            Matrix4x4 transMatrix = genTransMatrix(FlightGlobals.ActiveVessel.rootPart.transform, true);

            groundBelow = transMatrix.MultiplyPoint3x4(groundBelow);
            groundBelow1 = transMatrix.MultiplyPoint3x4(groundBelow1);
            groundBelow2 = transMatrix.MultiplyPoint3x4(groundBelow2);
            groundBelow3 = transMatrix.MultiplyPoint3x4(groundBelow3);
            groundBelow4 = transMatrix.MultiplyPoint3x4(groundBelow4);

            /*Quaternion rot = Quaternion.FromToRotation(groundN, Vector3.up);
            Quaternion rotInv = Quaternion.FromToRotation(Vector3.up, groundN);*/
            float angle = Vector3.Angle(Vector3.up, localSpaceNormal);
            if (customMode == null)
                {
                    if (basicSettings.displayGround == (int)ViewerConstants.GROUND_DISPMODE.PLANE) 
                    {
                        angle = Vector3.Angle(Vector3.back, localSpaceNormal);
                    }
                }
                else
                {
                    switch (customMode.MinimodesOverride)
                    {
                        case (int)CustomModeSettings.OVERRIDE_TYPES.AS_BASIC:
                            if (basicSettings.displayGround == (int)ViewerConstants.GROUND_DISPMODE.PLANE) 
                            {
                                angle = Vector3.Angle(Vector3.back, localSpaceNormal);
                            } break;
                        case (int)CustomModeSettings.OVERRIDE_TYPES.STATIC:
                            if (customMode.staticSettings.displayGround == (int)ViewerConstants.GROUND_DISPMODE.PLANE) 
                            {
                                angle = Vector3.Angle(Vector3.back, localSpaceNormal);
                            } break;
                        case (int)CustomModeSettings.OVERRIDE_TYPES.FUNCTION:
                            if (customMode.displayGroundDelegate(customMode) == (int)ViewerConstants.GROUND_DISPMODE.PLANE)
                            {
                                angle = Vector3.Angle(Vector3.back, localSpaceNormal);
                            } break;
                    }
                }
            
            if (angle > 40) angle = 40;
            //MonoBehaviour.print("angle> " + angle);
            Color color = genFractColor(1-(angle / 40f));
            //transMatrix = screenMatrix * transMatrix;
            //now render it

            //direction = transMatrix.MultiplyPoint3x4(direction);


            //groundBelow = FlightGlobals.ActiveVessel.vesselTransform.rotation.Inverse() * groundBelow;
            /*groundBelow1 = FlightGlobals.ActiveVessel.vesselTransform.rotation.Inverse() * groundBelow1;
            groundBelow2 = FlightGlobals.ActiveVessel.vesselTransform.rotation.Inverse() * groundBelow2;
            groundBelow3 = FlightGlobals.ActiveVessel.vesselTransform.rotation.Inverse() * groundBelow3;
            groundBelow4 = FlightGlobals.ActiveVessel.vesselTransform.rotation.Inverse() * groundBelow4;*/

            /*groundBelow = rot * groundBelow;
            groundBelow1 = rot * groundBelow1;
            groundBelow2 = rot * groundBelow2;
            groundBelow3 = rotInv * groundBelow3;
            groundBelow4 = rot * groundBelow4;*/

            

            /*MonoBehaviour.print("after>" + groundBelow);
            MonoBehaviour.print("after>" + groundBelow1);
            MonoBehaviour.print("after>" + groundBelow2);
            MonoBehaviour.print("after>" + groundBelow3);
            MonoBehaviour.print("after>" + groundBelow4);*/

            //MonoBehaviour.print("COM modified>" + COM);
            float div = 6 / basicSettings.scaleFact;
            renderIcon(new Rect(-div + groundBelow.x, -div + groundBelow.y, 2 * div, 2 * div), screenMatrix, Color.green, (int)ViewerConstants.ICONS.TRIANGLE_DOWN);
            //renderIcon(new Rect(-div + direction.x, -div + direction.y, 2 * div, 2 * div), screenMatrix, Color.magenta, (int)ViewerConstants.ICONS.DIAMOND);

            GL.Begin(GL.LINES);
            GL.Color(color);
            renderLine(groundBelow1.x, groundBelow1.y, groundBelow2.x, groundBelow2.y, screenMatrix);
            renderLine(groundBelow2.x, groundBelow2.y, groundBelow3.x, groundBelow3.y, screenMatrix);
            renderLine(groundBelow3.x, groundBelow3.y, groundBelow4.x, groundBelow4.y, screenMatrix);
            renderLine(groundBelow4.x, groundBelow4.y, groundBelow1.x, groundBelow1.y, screenMatrix);

            renderLine(groundBelow3.x, groundBelow3.y, groundBelow1.x, groundBelow1.y, screenMatrix);
            renderLine(groundBelow4.x, groundBelow4.y, groundBelow2.x, groundBelow2.y, screenMatrix);
            GL.End();

            if (groundBelow.x < minVecG.x) minVecG.x = groundBelow.x;
            if (groundBelow.y < minVecG.y) minVecG.y = groundBelow.y;
            if (groundBelow.z < minVecG.z) minVecG.z = groundBelow.z;
            if (groundBelow.x > maxVecG.x) maxVecG.x = groundBelow.x;
            if (groundBelow.y > maxVecG.y) maxVecG.y = groundBelow.y;
            if (groundBelow.z > maxVecG.z) maxVecG.z = groundBelow.z;
        }

        private void renderAxes(Matrix4x4 screenMatrix)
        {

            Matrix4x4 transMatrix = genTransMatrix(FlightGlobals.ActiveVessel.rootPart.transform, true);

            Vector3 up = transMatrix.MultiplyPoint3x4(Vector3.up * 10000);
            Vector3 down = transMatrix.MultiplyPoint3x4(Vector3.down * 10000);
            Vector3 left = transMatrix.MultiplyPoint3x4(Vector3.left * 10000);
            Vector3 right = transMatrix.MultiplyPoint3x4(Vector3.right * 10000);
            Vector3 front = transMatrix.MultiplyPoint3x4(Vector3.forward * 10000);
            Vector3 back = transMatrix.MultiplyPoint3x4(Vector3.back * 10000);

            GL.Begin(GL.LINES);
            GL.Color(Color.red);
            renderLine(left.x, left.y, right.x, right.y, screenMatrix);
            GL.End();

            GL.Begin(GL.LINES);
            GL.Color(Color.blue);
            renderLine(up.x, up.y, down.x, down.y, screenMatrix);
            GL.End();

            GL.Begin(GL.LINES);
            GL.Color(Color.green);
            renderLine(front.x, front.y, back.x, back.y, screenMatrix);
            GL.End();

        }

        private void renderCOM(Matrix4x4 screenMatrix)
        {
//            Vector3 COM = FlightGlobals.ActiveVessel.findLocalCenterOfMass();
            Vector3 COM = FlightGlobals.ActiveVessel.localCoM;
            //MonoBehaviour.print("COM>"+COM);
            Matrix4x4 transMatrix = genTransMatrix(FlightGlobals.ActiveVessel.rootPart.transform, true);
            //transMatrix = screenMatrix * transMatrix;
            //now render it
            COM = transMatrix.MultiplyPoint3x4(COM);
            //MonoBehaviour.print("COM modified>" + COM);
            float div = 6 / basicSettings.scaleFact;
            renderIcon(new Rect(-div + COM.x, -div + COM.y, 2 * div, 2 * div), screenMatrix, Color.magenta, (int)ViewerConstants.ICONS.SQUARE_DIAMOND);
        }
/*
        // Never used
        private void renderCOP(Matrix4x4 screenMatrix)
        {
//            Vector3 COP = FlightGlobals.ActiveVessel.findLocalCenterOfPressure();
            Vector3 COP = FlightGlobals.ActiveVessel.;
            //MonoBehaviour.print("COM>"+COM);
            Matrix4x4 transMatrix = genTransMatrix(FlightGlobals.ActiveVessel.rootPart.transform, FlightGlobals.ActiveVessel, true);
            //transMatrix = screenMatrix * transMatrix;
            //now render it
            COP = transMatrix.MultiplyPoint3x4(COP);
            //MonoBehaviour.print("COM modified>" + COM);
            float div = 6 / basicSettings.scaleFact;
            renderIcon(new Rect(-div + COP.x, -div + COP.y, 2 * div, 2 * div), screenMatrix, Color.cyan, (int)ViewerConstants.ICONS.SQUARE_DIAMOND);
        }

        // Never used
        private void renderMOI(Matrix4x4 screenMatrix)
        {
//            Vector3 MOI = FlightGlobals.ActiveVessel.findLocalMOI();
            Vector3 MOI = FlightGlobals.ActiveVessel.MOI;
            //MonoBehaviour.print("COM>"+COM);
            Matrix4x4 transMatrix = genTransMatrix(FlightGlobals.ActiveVessel.rootPart.transform, FlightGlobals.ActiveVessel, true);
            //transMatrix = screenMatrix * transMatrix;
            //now render it
            MOI = transMatrix.MultiplyPoint3x4(MOI);
            //MonoBehaviour.print("COM modified>" + COM);
            float div = 6 / basicSettings.scaleFact;
            renderIcon(new Rect(-div + MOI.x, -div + MOI.y, 2 * div, 2 * div), screenMatrix, Color.yellow, (int)ViewerConstants.ICONS.SQUARE_DIAMOND);
        }
*/

        public static Func<S, T> CreateGetter<S, T>(FieldInfo field)
        {
            string methodName = field.ReflectedType.FullName + ".get_" + field.Name;
            DynamicMethod setterMethod = new DynamicMethod(methodName, typeof(T), new Type[1] { typeof(S) }, true);
            ILGenerator gen = setterMethod.GetILGenerator();
            if (field.IsStatic)
            {
                gen.Emit(OpCodes.Ldsfld, field);
            }
            else
            {
                gen.Emit(OpCodes.Ldarg_0);
                gen.Emit(OpCodes.Ldfld, field);
            }
            gen.Emit(OpCodes.Ret);
            return (Func<S, T>)setterMethod.CreateDelegate(typeof(Func<S, T>));
        }

        public static Action<S, T> CreateSetter<S, T>(FieldInfo field)
        {
            string methodName = field.ReflectedType.FullName + ".set_" + field.Name;
            DynamicMethod setterMethod = new DynamicMethod(methodName, null, new Type[2] { typeof(S), typeof(T) }, true);
            ILGenerator gen = setterMethod.GetILGenerator();
            if (field.IsStatic)
            {
                gen.Emit(OpCodes.Ldarg_1);
                gen.Emit(OpCodes.Stsfld, field);
            }
            else
            {
                gen.Emit(OpCodes.Ldarg_0);
                gen.Emit(OpCodes.Ldarg_1);
                gen.Emit(OpCodes.Stfld, field);
            }
            gen.Emit(OpCodes.Ret);
            return (Action<S, T>)setterMethod.CreateDelegate(typeof(Action<S, T>));
        }

        // This could all be simpler and faster if we used a publicizer instead
        static FieldInfo x_Part_modelMeshRenderersCache_FieldInfo = typeof(Part).GetField("modelMeshRenderersCache", BindingFlags.Instance | BindingFlags.NonPublic);
        static FieldInfo x_Part_modelSkinnedMeshRenderersCache_FieldInfo = typeof(Part).GetField("modelSkinnedMeshRenderersCache", BindingFlags.Instance | BindingFlags.NonPublic);

        static Func<Part, List<MeshRenderer>> Part_GetMeshRenderers = CreateGetter<Part, List<MeshRenderer>>(x_Part_modelMeshRenderersCache_FieldInfo);
        static Func<Part, List<SkinnedMeshRenderer>> Part_GetSkinnedMeshRenderers = CreateGetter<Part, List<SkinnedMeshRenderer>>(x_Part_modelSkinnedMeshRenderersCache_FieldInfo);
        static Action<Part, List<MeshRenderer>> Part_SetMeshRenderers = CreateSetter<Part, List<MeshRenderer>>(x_Part_modelMeshRenderersCache_FieldInfo);
        static Action<Part, List<SkinnedMeshRenderer>> Part_SetSkinnedMeshRenderers = CreateSetter<Part, List<SkinnedMeshRenderer>>(x_Part_modelSkinnedMeshRenderersCache_FieldInfo);

        Color GetPartColor(Part part, bool fill)
        {
            Color partColor = new Color();

            if (customMode == null)
            {
                partColor = getPartColor(part, fill ? basicSettings.colorModeFill : basicSettings.colorModeWire);
            }
            else
            {
                switch (customMode.ColorModeOverride)
                {
                    case (int)CustomModeSettings.OVERRIDE_TYPES.AS_BASIC:
                        partColor = getPartColor(part, fill ? basicSettings.colorModeFill : basicSettings.colorModeWire);
                        break;
                    case (int)CustomModeSettings.OVERRIDE_TYPES.STATIC:
                        partColor = getPartColor(part, fill ? customMode.staticSettings.colorModeFill : customMode.staticSettings.colorModeWire);
                        break;
                    case (int)CustomModeSettings.OVERRIDE_TYPES.FUNCTION:
                        partColor = fill ? customMode.fillColorDelegate(customMode, part) : customMode.wireColorDelegate(customMode, part);
                        break;
                }
            }

            bool dull = false;

            if (customMode == null)
            {
                dull = fill ? basicSettings.colorModeFillDull : basicSettings.colorModeWireDull;
            }
            else
            {
                switch (customMode.ColorModeOverride)
                {
                    case (int)CustomModeSettings.OVERRIDE_TYPES.AS_BASIC:
                        dull = fill ? basicSettings.colorModeFillDull : basicSettings.colorModeWireDull;
                        break;
                    case (int)CustomModeSettings.OVERRIDE_TYPES.STATIC:
                        dull = fill ? customMode.staticSettings.colorModeFillDull : customMode.staticSettings.colorModeWireDull;
                        break;
                    case (int)CustomModeSettings.OVERRIDE_TYPES.FUNCTION:
                        dull = fill ? customMode.fillColorDullDelegate(customMode) : customMode.wireColorDullDelegate(customMode);
                        break;
                }
            }

            if (dull)
            {
                partColor.r /= 2;
                partColor.g /= 2;
                partColor.b /= 2;
            }

            return partColor;
        }

        Color GetBoxColor(Part part)
        {
            Color boxColor = new Color();

            if (customMode == null)
            {
                boxColor = getPartColor(part, basicSettings.colorModeBox);
            }
            else
            {
                switch (customMode.ColorModeOverride)
                {
                    case (int)CustomModeSettings.OVERRIDE_TYPES.AS_BASIC:
                        boxColor = getPartColor(part, basicSettings.colorModeBox);
                        break;
                    case (int)CustomModeSettings.OVERRIDE_TYPES.STATIC:
                        boxColor = getPartColor(part, customMode.staticSettings.colorModeBox);
                        break;
                    case (int)CustomModeSettings.OVERRIDE_TYPES.FUNCTION:
                        boxColor = customMode.boxColorDelegate(customMode, part);
                        break;
                }
            }

            bool dull = false;

            if (customMode == null)
            {
                dull = basicSettings.colorModeBoxDull;
            }
            else
            {
                switch (customMode.ColorModeOverride)
                {
                    case (int)CustomModeSettings.OVERRIDE_TYPES.AS_BASIC:
                        dull = basicSettings.colorModeBoxDull;
                        break;
                    case (int)CustomModeSettings.OVERRIDE_TYPES.STATIC:
                        dull = customMode.staticSettings.colorModeBoxDull;
                        break;
                    case (int)CustomModeSettings.OVERRIDE_TYPES.FUNCTION:
                        dull = customMode.boxColorDullDelegate(customMode);
                        break;
                }
            }

            if (dull)
            {
                boxColor.r = boxColor.r / 2;
                boxColor.g = boxColor.g / 2;
                boxColor.b = boxColor.b / 2;
            }

            return boxColor;
        }

        /// <summary>
        /// Renders a single part and adds all its children to the draw queue.
        /// Also adds its bounding box to the bounding box queue.
        /// </summary>
        /// <param name="part">Part to render</param>
        /// <param name="scrnMatrix">Screen transform</param>
        private void renderPart(Part part, Matrix4x4 scrnMatrix)
        {
            //first off, add all the parts children to the queue
            foreach (Part child in part.children)
            {
                if (!child.Equals(part.parent))
                {
                    partQueue.Enqueue(child);
                }
            }
            
            //get the appropriate colors
            Color fillPartColor = GetPartColor(part, true);
            Color wirePartColor = GetPartColor(part, false);
            Color boxColor = GetBoxColor(part);

            //used to determine the part bounding box
            Vector3 minVec = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 maxVec = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            //now we need to get all meshes in the part
            var meshRenderers = Part_GetMeshRenderers(part);
            if (meshRenderers == null)
            {
                meshRenderers = part.FindModelComponents<MeshRenderer>();
                Part_SetMeshRenderers(part, meshRenderers);
            }

            foreach (MeshRenderer renderer in meshRenderers)
            {
                if (renderer == null || renderer.gameObject.layer == TransparentFxLayer) continue;

                MeshFilter meshF = renderer.GetComponent<MeshFilter>();

                //only render those meshes that are active
                //examples of inactive meshes seem to include
                //parachute canopies, engine fairings...

                if (renderer.gameObject.activeInHierarchy && meshF != null)
                {
                    Mesh mesh = meshF.mesh;
                    //create the trans. matrix for this mesh (also update the bounds)
                    Matrix4x4 transMatrix = worldToScreenFlattened * meshF.transform.localToWorldMatrix;
                    updateMinMax(mesh.bounds, transMatrix, ref minVec, ref maxVec);
                    transMatrix = scrnMatrix * transMatrix;
                    //now render it
                    if (fillPartColor.a != 0)
                    {
                        GL.wireframe = false;
                        renderMesh(mesh, transMatrix, fillPartColor);
                    }
                    if (wirePartColor.a != 0)
                    {
                        GL.wireframe = true;
                        renderMesh(mesh, transMatrix, wirePartColor);
                    }
                }
            }


            var skinnedMeshRenderers = Part_GetSkinnedMeshRenderers(part);
            if (skinnedMeshRenderers == null)
            {
                skinnedMeshRenderers = part.FindModelComponents<SkinnedMeshRenderer>();
                Part_SetSkinnedMeshRenderers(part, skinnedMeshRenderers);
            }

            foreach (SkinnedMeshRenderer smesh in skinnedMeshRenderers)
            {
                if (smesh == null || smesh.gameObject.layer == TransparentFxLayer) continue;

                if (smesh.gameObject.activeInHierarchy)
                {
                    //skinned meshes seem to be not nearly as conveniently simple
                    //luckily, I can apparently ask them to do all the work for me
                    smesh.BakeMesh(bakedMesh); // TODO: I'm sure this is super slow - can we cache the baked mesh if it's not animating?
                    //create the trans. matrix for this mesh (also update the bounds)
                    Matrix4x4 scalingTransform = Matrix4x4.Scale(new Vector3(1.0f / smesh.transform.lossyScale.x, 1.0f / smesh.transform.lossyScale.y, 1.0f / smesh.transform.lossyScale.z));
                    Matrix4x4 transMatrix = worldToScreenFlattened * (smesh.transform.localToWorldMatrix * scalingTransform);
                    updateMinMax(bakedMesh.bounds, transMatrix, ref minVec, ref maxVec);
                    transMatrix = scrnMatrix * transMatrix;
                    //now render it
                    if (fillPartColor.a != 0)
                    {
                        GL.wireframe = false;
                        renderMesh(bakedMesh, transMatrix, fillPartColor);
                    }
                    if (wirePartColor.a != 0)
                    {
                        GL.wireframe = true;
                        renderMesh(bakedMesh, transMatrix, wirePartColor);
                    }
                }
            }

            bool addToTotals = false;
            if (customMode == null) addToTotals = true;
            else if (customMode.focusSubset.Count == 0) addToTotals = true;
            else if (customMode.focusSubset.Contains(part)) addToTotals = true;

            if(addToTotals)
            {
                //finally, update the vessel "bounding box"
                if (minVecG.x > minVec.x) minVecG.x = minVec.x;
                if (minVecG.y > minVec.y) minVecG.y = minVec.y;
                if (minVecG.z > minVec.z) minVecG.z = minVec.z;
                if (maxVecG.x < maxVec.x) maxVecG.x = maxVec.x;
                if (maxVecG.y < maxVec.y) maxVecG.y = maxVec.y;
                if (maxVecG.z < maxVec.z) maxVecG.z = maxVec.z;
            }
            
            //and draw a box around the part (later)
            rectQueue.Enqueue(new ViewerConstants.RectColor(new Rect((minVec.x), (minVec.y), (maxVec.x - minVec.x), (maxVec.y - minVec.y)), boxColor));
        }

        /// <summary>
        /// Renders a mesh. Doesnt work?
        /// </summary>
        /// <param name="transMatrix">Mesh transform.</param>
        /// <param name="color">Color.</param>
        private void renderMesh(Mesh mesh, Matrix4x4 transMatrix, Color color)
        {
            //setup GL
            GL.PushMatrix();
            GL.MultMatrix(transMatrix);
            lineMaterial.color = color;
            lineMaterial.SetPass(0);
            //and draw the triangles
            //TODO: Maybe it doesnt have to be done in immediate mode?
            //Unity GL doesnt seem to expose much, though.
            Graphics.DrawMeshNow(mesh, transMatrix);
            GL.PopMatrix();
        }

        private void renderCone(Transform thrustTransform, float scale, float offset, Matrix4x4 screenMatrix, Color color)
        {
            float timeAdd = (Time.frameCount % 40);
            if(timeAdd < 20)
            {
                scale += (scale / 100) * timeAdd;
            }
            else
            {
                scale += (scale / 100) * (40-timeAdd);
            }
            float sideScale = scale / 4f;
            Matrix4x4 transMatrix = genTransMatrix(thrustTransform, true);
            Vector3 posStr = new Vector3(0, 0, offset);
            posStr = transMatrix.MultiplyPoint3x4(posStr);
            Vector3 posStr1 = new Vector3(-sideScale, 0, offset+sideScale);
            posStr1 = transMatrix.MultiplyPoint3x4(posStr1);
            Vector3 posStr2 = new Vector3(0, -sideScale, offset+sideScale);
            posStr2 = transMatrix.MultiplyPoint3x4(posStr2);
            Vector3 posStr3 = new Vector3(sideScale, 0, offset+sideScale);
            posStr3 = transMatrix.MultiplyPoint3x4(posStr3);
            Vector3 posStr4 = new Vector3(0, sideScale, offset+sideScale);
            posStr4 = transMatrix.MultiplyPoint3x4(posStr4);
            Vector3 posEnd = new Vector3(0, 0, offset+scale);
            posEnd = transMatrix.MultiplyPoint3x4(posEnd);
            //setup GL, then render the lines
            GL.Begin(GL.LINES);
            GL.Color(color);
            renderLine(posStr1.x, posStr1.y, posStr2.x, posStr2.y, screenMatrix);
            renderLine(posStr2.x, posStr2.y, posStr3.x, posStr3.y, screenMatrix);
            renderLine(posStr3.x, posStr3.y, posStr4.x, posStr4.y, screenMatrix);
            renderLine(posStr4.x, posStr4.y, posStr1.x, posStr1.y, screenMatrix);
            renderLine(posStr1.x, posStr1.y, posEnd.x, posEnd.y, screenMatrix);
            renderLine(posStr2.x, posStr2.y, posEnd.x, posEnd.y, screenMatrix);
            renderLine(posStr3.x, posStr3.y, posEnd.x, posEnd.y, screenMatrix);
            renderLine(posStr4.x, posStr4.y, posEnd.x, posEnd.y, screenMatrix);
            renderLine(posStr1.x, posStr1.y, posStr.x, posStr.y, screenMatrix);
            renderLine(posStr2.x, posStr2.y, posStr.x, posStr.y, screenMatrix);
            renderLine(posStr3.x, posStr3.y, posStr.x, posStr.y, screenMatrix);
            renderLine(posStr4.x, posStr4.y, posStr.x, posStr.y, screenMatrix);
            GL.End();
        }

        /// <summary>
        /// Renders a gui icon.
        /// </summary>
        /// <param name="rect">Rectangle.</param>
        /// <param name="screenMatrix">Transformation matrix.</param>
        /// <param name="color">Color.</param>
        private void renderIcon(Rect rect, Matrix4x4 screenMatrix, Color color, int type)
        {
            GL.Begin(GL.QUADS);
            GL.Color(Color.black);
            lineMaterial.color = Color.black;
            lineMaterial.SetPass(0);
            GL.wireframe = false;
            GL.Vertex(screenMatrix.MultiplyPoint3x4(new Vector3(rect.xMin, rect.yMin, 0.1f)));
            GL.Vertex(screenMatrix.MultiplyPoint3x4(new Vector3(rect.xMin, rect.yMax, 0.1f)));
            GL.Vertex(screenMatrix.MultiplyPoint3x4(new Vector3(rect.xMax, rect.yMax, 0.1f)));
            GL.Vertex(screenMatrix.MultiplyPoint3x4(new Vector3(rect.xMax, rect.yMin, 0.1f)));
            GL.End();
            GL.wireframe = true;
            
            //setup GL, then render the lines
            GL.Begin(GL.LINES);
            GL.Color(color);
            lineMaterial.color = color;
            lineMaterial.SetPass(0);
            float xMid = ((rect.xMax - rect.xMin) / 2) + rect.xMin;
            float yMid = ((rect.yMax - rect.yMin) / 2) + rect.yMin;
            float xOneFourth = ((xMid - rect.xMin) / 2) + rect.xMin;
            float yOneFourth = ((yMid - rect.yMin) / 2) + rect.yMin;
            float xThreeFourth = ((rect.xMax - xMid) / 2) + xMid;
            float yThreeFourth = ((rect.yMax - yMid) / 2) + yMid;
            switch (type) 
            {
                case (int)ViewerConstants.ICONS.SQUARE:
                    renderLine(rect.xMin, rect.yMin, rect.xMax, rect.yMin, screenMatrix);
                    renderLine(rect.xMax, rect.yMin, rect.xMax, rect.yMax, screenMatrix);
                    renderLine(rect.xMax, rect.yMax, rect.xMin, rect.yMax, screenMatrix);
                    renderLine(rect.xMin, rect.yMax, rect.xMin, rect.yMin, screenMatrix);
                    break;
                case (int)ViewerConstants.ICONS.DIAMOND:
                    renderLine(xMid, rect.yMin, rect.xMax, yMid, screenMatrix);
                    renderLine(rect.xMax, yMid, xMid, rect.yMax, screenMatrix);
                    renderLine(xMid, rect.yMax, rect.xMin, yMid, screenMatrix);
                    renderLine(rect.xMin, yMid, xMid, rect.yMin, screenMatrix);
                    break;
                case (int)ViewerConstants.ICONS.SQUARE_DIAMOND:
                    renderLine(rect.xMin, rect.yMin, rect.xMax, rect.yMin, screenMatrix);
                    renderLine(rect.xMax, rect.yMin, rect.xMax, rect.yMax, screenMatrix);
                    renderLine(rect.xMax, rect.yMax, rect.xMin, rect.yMax, screenMatrix);
                    renderLine(rect.xMin, rect.yMax, rect.xMin, rect.yMin, screenMatrix);
                    renderLine(xMid, rect.yMin, rect.xMax, yMid, screenMatrix);
                    renderLine(rect.xMax, yMid, xMid, rect.yMax, screenMatrix);
                    renderLine(xMid, rect.yMax, rect.xMin, yMid, screenMatrix);
                    renderLine(rect.xMin, yMid, xMid, rect.yMin, screenMatrix);
                    break;
                case (int)ViewerConstants.ICONS.TRIANGLE_UP:
                    renderLine(rect.xMin, rect.yMin, rect.xMax, rect.yMin, screenMatrix);
                    renderLine(rect.xMax, rect.yMin, xMid, rect.yMax, screenMatrix);
                    renderLine(xMid, rect.yMax, rect.xMin, rect.yMin, screenMatrix);
                    break;
                case (int)ViewerConstants.ICONS.TRIANGLE_DOWN:
                    renderLine(rect.xMin, rect.yMax, rect.xMax, rect.yMax, screenMatrix);
                    renderLine(rect.xMax, rect.yMax, xMid, rect.yMin, screenMatrix);
                    renderLine(xMid, rect.yMin, rect.xMin, rect.yMax, screenMatrix);
                    break;
                case (int)ViewerConstants.ICONS.ENGINE_READY:
                    renderLine(rect.xMin, rect.yMin, rect.xMax, rect.yMin, screenMatrix);
                    renderLine(rect.xMax, rect.yMin, rect.xMax, rect.yMax, screenMatrix);
                    renderLine(rect.xMax, rect.yMax, rect.xMin, rect.yMax, screenMatrix);
                    renderLine(rect.xMin, rect.yMax, rect.xMin, rect.yMin, screenMatrix);

                    renderLine(rect.xMin, yMid, xMid, rect.yMin, screenMatrix);
                    renderLine(xMid, rect.yMin, rect.xMax, rect.yMax, screenMatrix);
                    break;
                case (int)ViewerConstants.ICONS.ENGINE_NOPOWER:
                    renderLine(rect.xMin, rect.yMin, rect.xMax, rect.yMin, screenMatrix);
                    renderLine(rect.xMax, rect.yMin, rect.xMax, rect.yMax, screenMatrix);
                    renderLine(rect.xMax, rect.yMax, rect.xMin, rect.yMax, screenMatrix);
                    renderLine(rect.xMin, rect.yMax, rect.xMin, rect.yMin, screenMatrix);

                    renderLine(xMid, rect.yMin, xThreeFourth, yMid, screenMatrix);
                    renderLine(xOneFourth, yMid, xThreeFourth, yMid, screenMatrix);
                    renderLine(xOneFourth, yMid, xMid, rect.yMax, screenMatrix);
                    break;
                case (int)ViewerConstants.ICONS.ENGINE_NOFUEL:
                    renderLine(rect.xMin, rect.yMin, rect.xMax, rect.yMin, screenMatrix);
                    renderLine(rect.xMax, rect.yMin, rect.xMax, rect.yMax, screenMatrix);
                    renderLine(rect.xMax, rect.yMax, rect.xMin, rect.yMax, screenMatrix);
                    renderLine(rect.xMin, rect.yMax, rect.xMin, rect.yMin, screenMatrix);

                    renderLine(rect.xMin, rect.yMin, rect.xMax, rect.yMax, screenMatrix);
                    renderLine(rect.xMin, rect.yMax, rect.xMax, rect.yMin, screenMatrix);
                    break;
                case (int)ViewerConstants.ICONS.ENGINE_NOAIR:
                    renderLine(rect.xMin, rect.yMin, rect.xMax, rect.yMin, screenMatrix);
                    renderLine(rect.xMax, rect.yMin, rect.xMax, rect.yMax, screenMatrix);
                    renderLine(rect.xMax, rect.yMax, rect.xMin, rect.yMax, screenMatrix);
                    renderLine(rect.xMin, rect.yMax, rect.xMin, rect.yMin, screenMatrix);

                    renderLine(xOneFourth, yMid, xThreeFourth, yMid, screenMatrix);
                    renderLine(xMid, yOneFourth, xMid, yThreeFourth, screenMatrix);

                    renderLine(xOneFourth, yOneFourth, xThreeFourth, yThreeFourth, screenMatrix);
                    renderLine(xOneFourth, yThreeFourth, xThreeFourth, yOneFourth, screenMatrix);
                    break;
                case (int)ViewerConstants.ICONS.ENGINE_ACTIVE:
                    renderLine(rect.xMin, rect.yMin, rect.xMax, rect.yMin, screenMatrix);
                    renderLine(rect.xMax, rect.yMin, rect.xMax, rect.yMax, screenMatrix);
                    renderLine(rect.xMax, rect.yMax, rect.xMin, rect.yMax, screenMatrix);
                    renderLine(rect.xMin, rect.yMax, rect.xMin, rect.yMin, screenMatrix);

                    renderLine(xMid, rect.yMin, xOneFourth, yThreeFourth, screenMatrix);
                    renderLine(xMid, rect.yMin, xThreeFourth, yThreeFourth, screenMatrix);
                    renderLine(xMid, rect.yMax, xOneFourth, yThreeFourth, screenMatrix);
                    renderLine(xMid, rect.yMax, xThreeFourth, yThreeFourth, screenMatrix);
                    break;
                case (int)ViewerConstants.ICONS.ENGINE_INACTIVE:
                    renderLine(rect.xMin, rect.yMin, rect.xMax, rect.yMin, screenMatrix);
                    renderLine(rect.xMax, rect.yMin, rect.xMax, rect.yMax, screenMatrix);
                    renderLine(rect.xMax, rect.yMax, rect.xMin, rect.yMax, screenMatrix);
                    renderLine(rect.xMin, rect.yMax, rect.xMin, rect.yMin, screenMatrix);

                    renderLine(rect.xMin, rect.yMin, rect.xMax, rect.yMax, screenMatrix);
                    break;
            }
            GL.End();
        }

        /// <summary>
        /// Renders a rectangle.
        /// </summary>
        /// <param name="rect">Rectangle.</param>
        /// <param name="screenMatrix">Transformation matrix.</param>
        /// <param name="color">Color.</param>
        private void renderRect(Rect rect, Matrix4x4 screenMatrix, Color color)
        {
            //setup GL, then render the lines
            GL.Begin(GL.LINES);
            GL.Color(color);
            lineMaterial.color = color;
            lineMaterial.SetPass(0);
            renderLine(rect.xMin, rect.yMin, rect.xMax, rect.yMin, screenMatrix);
            renderLine(rect.xMax, rect.yMin, rect.xMax, rect.yMax, screenMatrix);
            renderLine(rect.xMax, rect.yMax, rect.xMin, rect.yMax, screenMatrix);
            renderLine(rect.xMin, rect.yMax, rect.xMin, rect.yMin, screenMatrix);
            GL.End();
        }

        /// <summary>
        /// Renders a line. Assumes color was set already.
        /// </summary>
        /// <param name="x1">x1</param>
        /// <param name="y1">y1</param>
        /// <param name="x2">x2</param>
        /// <param name="y2">x2</param>
        /// <param name="screenMatrix">Screen transformation matrix</param>
        private void renderLine(float x1, float y1, float x2, float y2, Matrix4x4 screenMatrix)
        {
            Vector3 v1 = screenMatrix.MultiplyPoint3x4(new Vector3(x1, y1, 0.1f));
            Vector3 v2 = screenMatrix.MultiplyPoint3x4(new Vector3(x2, y2, 0.1f));
            GL.Vertex(v1);
            GL.Vertex(v2);
        }

        /// <summary>
        /// Updates the min and max values for the total part bounding box.
        /// Uses the mesh bounding box.
        /// </summary>
        /// <param name="meshBounds">Mesh bounding box.</param>
        /// <param name="transMatrix">Mesh transform</param>
        /// <param name="minVec">Reference to minimums-so-far vector</param>
        /// <param name="maxVec">Reference to maximums-so-far vector</param>
        private void updateMinMax(Bounds meshBounds, Matrix4x4 transMatrix, ref Vector3 minVec, ref Vector3 maxVec)
        {
            Vector3 v1 = new Vector3(transMatrix.m00, transMatrix.m10, transMatrix.m20) * meshBounds.extents.x;
            Vector3 v2 = new Vector3(transMatrix.m01, transMatrix.m11, transMatrix.m21) * meshBounds.extents.y;
            Vector3 v3 = new Vector3(transMatrix.m02, transMatrix.m12, transMatrix.m22) * meshBounds.extents.z;

            Vector3 newCenter = transMatrix.MultiplyPoint(meshBounds.center);
            Vector3 newExtents = new Vector3(
                Mathf.Abs(v1.x) + Mathf.Abs(v2.x) + Mathf.Abs(v3.x),
                Mathf.Abs(v1.y) + Mathf.Abs(v2.y) + Mathf.Abs(v3.y),
                Mathf.Abs(v1.z) + Mathf.Abs(v2.z) + Mathf.Abs(v3.z));

            minVec = Vector3.Min(minVec, newCenter - newExtents);
            maxVec = Vector3.Max(maxVec, newCenter + newExtents);
        }

        /// <summary>
        /// Generate a transform matrix from the meshes and vessels matrix
        /// </summary>
        /// <param name="meshTrans">Mesh matrix</param>
        /// <param name="vessel">Active vessel</param>
        /// <returns></returns>
        private Matrix4x4 genTransMatrix(Transform meshTrans, bool zeroFlatter)
        {
            return (zeroFlatter ? worldToScreen : worldToScreenFlattened) * meshTrans.localToWorldMatrix;
        }

        /// <summary>
        /// Calculate the ideal scale/offset.
        /// </summary>
        private void centerise(int screenWidth, int screenHeight)
        {
            //for padding
            float margin = 1f;
            bool centerH = false;
            bool centerV = false;
            int rescale = 0;
            if (customMode == null)
                {
                    margin = ViewerConstants.MARGIN_MULTIPLIER[basicSettings.margin];
                    centerH = basicSettings.centerOnRootH;
                    centerV = basicSettings.centerOnRootV;
                    rescale = basicSettings.centerRescale;
                }
                else
                {
                    switch (customMode.CenteringOverride)
                    {
                        case (int)CustomModeSettings.OVERRIDE_TYPES.AS_BASIC:
                            margin = ViewerConstants.MARGIN_MULTIPLIER[basicSettings.margin];
                            centerH = basicSettings.centerOnRootH;
                            centerV = basicSettings.centerOnRootV;
                            rescale = basicSettings.centerRescale;
                            break;
                        case (int)CustomModeSettings.OVERRIDE_TYPES.STATIC:
                            margin = ViewerConstants.MARGIN_MULTIPLIER[customMode.staticSettings.margin];
                            centerH = customMode.staticSettings.centerOnRootH;
                            centerV = customMode.staticSettings.centerOnRootV;
                            rescale = customMode.staticSettings.centerRescale;
                            break;
                        case (int)CustomModeSettings.OVERRIDE_TYPES.FUNCTION:
                            margin = customMode.marginDelegate(customMode);
                            centerH = customMode.centerOnRootHDelegate(customMode);
                            centerV = customMode.centerOnRootVDelegate(customMode);
                            rescale = customMode.centerRescaleDelegate(customMode);
                            break;
                    }
                }
            float screenWidthM = screenWidth * margin;
            float screenHeightM = screenHeight * margin;
            //if we want the root part to stay in the center on an axis, we need the 
            //bounding box to have the same size from both sides of it in that axis
            if (centerH)
            {
                if (Math.Abs(maxVecG.x) < Math.Abs(minVecG.x)) maxVecG.x = -minVecG.x;
                else minVecG.x = -maxVecG.x;
            }
            if (centerV)
            {
                if (Math.Abs(maxVecG.y) < Math.Abs(minVecG.y)) maxVecG.y = -minVecG.y;
                else minVecG.y = -maxVecG.y;
            }
            float xDiff = (maxVecG.x - minVecG.x);
            float yDiff = (maxVecG.y - minVecG.y);
            //to rescale, we need to scale up the vessel render to fit the screen bounds
            if (rescale != (int)ViewerConstants.RESCALEMODE.OFF)
            {
                float maxDiff = 0;
                if (rescale == (int)ViewerConstants.RESCALEMODE.INCR) maxDiff = 0.5f;
                if (rescale == (int)ViewerConstants.RESCALEMODE.CLOSE) maxDiff = 0.85f;
                if (rescale == (int)ViewerConstants.RESCALEMODE.BEST) maxDiff = 1f;

                float idealScaleX = screenWidthM / xDiff;
                float idealScaleY = screenHeightM / yDiff;
                //round to nearest integer
                float newScale = (int)((idealScaleX < idealScaleY) ? idealScaleX : idealScaleY);
                float diffFact = basicSettings.scaleFact / newScale;
                if (diffFact < maxDiff | diffFact > 1) 
                {
                    basicSettings.scaleFact = newScale;
                    //and clamp it a bit
                    if (basicSettings.scaleFact < 1) basicSettings.scaleFact = 1;
                    if (basicSettings.scaleFact > 1000) basicSettings.scaleFact = 1000;
                }
            }
            //to centerise, we need to move the center point of the vessel render
            //into the center of the screen
            basicSettings.scrOffX = screenWidth / 2 - (int)((minVecG.x + xDiff / 2) * basicSettings.scaleFact);
            basicSettings.scrOffY = screenHeight / 2 - (int)((minVecG.y + yDiff / 2) * basicSettings.scaleFact);
        }

        

        /// <summary>
        /// Returns the color appropriate for a given part,
        /// depending on the coloring mode provided.
        /// </summary>
        /// <param name="part">Associated part.</param>
        /// <param name="mode">Coloring mode.</param>
        /// <returns></returns>
        private Color getPartColor(Part part, int mode)
        {
            switch (mode)
            {
                case (int)ViewerConstants.COLORMODE.NONE:
                    return Color.white;
                case (int)ViewerConstants.COLORMODE.STATE:
                    //it seems most of these are unused, but it does at least
                    //make it (semi-)clear what is the root part and which parts belong
                    //to activated stages
                    if (part.parent == null) { return Color.magenta; }
                    switch (part.State)
                    {
                        case PartStates.ACTIVE:
                            return Color.blue;
                        case PartStates.DEACTIVATED:
                            return Color.red;
                        case PartStates.DEAD:
                            return Color.gray;
                        case PartStates.IDLE:
                            return Color.green;
                        default:
                            return Color.red;
                    }
                case (int)ViewerConstants.COLORMODE.STAGE:
                    //colors the parts by their inverse stage.
                    //first we need an appropriate gradient, so check if we have it
                    //and make it if we dont, or if its too small
                    if (stagesThisTimeMax < part.inverseStage) stagesThisTimeMax = part.inverseStage;

                    int neededColors = Math.Max(stagesLastTime, Math.Max(StageManager.StageCount, stagesThisTimeMax)) + 1;
                    if (stageGradient == null)
                    {
                        stageGradient = genColorGradient(neededColors);
                    }
                    else if (stageGradient.Length != neededColors)
                    {
                        stageGradient = genColorGradient(neededColors);
                    }
                    //now return the color
                    //print("part " + part.name + " inv. stage " + part.inverseStage);
                    if ((part.inverseStage < 0) || (part.inverseStage >= stageGradient.Length)) return Color.magenta;
                    return stageGradient[part.inverseStage];
                case (int)ViewerConstants.COLORMODE.HEAT:
                    //colors the part according to how close its to exploding due to overheat
                    Color color = new Color(0.2f, 0.2f, 0.2f);
                    if ((part.maxTemp != 0) && (part.skinMaxTemp != 0))
                    {
                        double tempDiff = (part.temperature > part.skinTemperature) ? (part.temperature / part.maxTemp) : (part.skinTemperature / part.skinMaxTemp);
                        //to power of THREE to emphasise overheating parts MORE
                        tempDiff = Math.Pow(tempDiff, 3);
                        //color.g = 0.2f;
                        color.b = (float)(0.2f * (1 - tempDiff));
                        color.r = (float)(0.2f + tempDiff * 0.8f);
                        return color;
                    }
                    else
                    {
                        return color;
                    }
                case (int)ViewerConstants.COLORMODE.FUEL:
                    Color color2 = Color.red;
                    int resCount = part.Resources.dict.Count;
                    int emptyRes = 0;
                    double totalResFraction = 0;
                    //go through all the resources in the part, add up their fullness
                    foreach (PartResource resource in part.Resources.dict.Values)
                    {
                        //2 is almost empty anyway for all but the smallest tanks 
                        //and it eliminates things like intakes or power generating engines
                        if (resource.amount <= 2f)
                        {
                            emptyRes++;
                        }
                        else
                        {
                            double resourceFraction = (resource.amount / resource.maxAmount);
                            totalResFraction += resourceFraction / (double)resCount;
                        }
                    }
                    //now set the part color
                    if (resCount == 0 | emptyRes == resCount)
                    {
                        color2 = new Color(0.2f, 0.2f, 0.2f);
                    }
                    else
                    {
                        return genFractColor((float)totalResFraction);
                    }

                    return color2;
                case (int)ViewerConstants.COLORMODE.DRAG:
                    float drag = part.angularDrag;
                    if (part.Modules.Contains("FARControllableSurface"))
                    {
                        //MonoBehaviour.print("cont. surf.");
                        PartModule FARmodule = part.Modules["FARControllableSurface"];
                        foreach (BaseField fieldInList in FARmodule.Fields)
                        {
                            if (fieldInList.name.Equals("currentDrag"))
                            {
                                drag = (float)fieldInList.GetValue(FARmodule);
                                break;
                            }
                        }
                    }
                    else if (part.Modules.Contains("FARWingAerodynamicModel"))
                    {
                        //MonoBehaviour.print("wing");
                        PartModule FARmodule = part.Modules["FARWingAerodynamicModel"];
                        foreach (BaseField fieldInList in FARmodule.Fields)
                        {
                            if (fieldInList.name.Equals("currentDrag"))
                            {
                                drag = (float)fieldInList.GetValue(FARmodule);
                                break;
                            }
                        }
                    }  
                    else if (part.Modules.Contains("FARBasicDragModel"))
                    {
                        //MonoBehaviour.print("basic drag");
                        PartModule FARmodule = part.Modules["FARBasicDragModel"];
                        foreach (BaseField fieldInList in FARmodule.Fields) 
                        {
                            if (fieldInList.name.Equals("currentDrag")) 
                            {
                                drag = (float)fieldInList.GetValue(FARmodule);
                                break;
                            }
                        }
                    }
                    return genHeatmapColor(drag);
                case (int)ViewerConstants.COLORMODE.LIFT:
                    float lift = 0;
                    if (part.Modules.Contains("FARControllableSurface"))
                    {
                        //MonoBehaviour.print("cont. surf.");
                        PartModule FARmodule = part.Modules["FARControllableSurface"];
                        foreach (BaseField fieldInList in FARmodule.Fields)
                        {
                            if (fieldInList.name.Equals("currentLift"))
                            {
                                lift = (float)fieldInList.GetValue(FARmodule);
                                break;
                            }
                        }
                    }
                    else if (part.Modules.Contains("FARWingAerodynamicModel"))
                    {
                        //MonoBehaviour.print("wing");
                        PartModule FARmodule = part.Modules["FARWingAerodynamicModel"];
                        foreach (BaseField fieldInList in FARmodule.Fields)
                        {
                            if (fieldInList.name.Equals("currentLift"))
                            {
                                lift = (float)fieldInList.GetValue(FARmodule);
                                break;
                            }
                        }
                    }
                    return genHeatmapColor(lift);
                case (int)ViewerConstants.COLORMODE.STALL:
                    float stall = 0;
                    if (part.Modules.Contains("FARControllableSurface"))
                    {
                        //MonoBehaviour.print("cont. surf.");
                        PartModule FARmodule = part.Modules["FARControllableSurface"];
                        foreach (BaseField fieldInList in FARmodule.Fields)
                        {
                            if (fieldInList.name.Equals("stall"))
                            {
                                stall = (float)fieldInList.GetValue(FARmodule);
                                break;
                            }
                        }
                    }
                    else if (part.Modules.Contains("FARWingAerodynamicModel"))
                    {
                        //MonoBehaviour.print("wing");
                        PartModule FARmodule = part.Modules["FARWingAerodynamicModel"];
                        foreach (BaseField fieldInList in FARmodule.Fields)
                        {
                            if (fieldInList.name.Equals("stall"))
                            {
                                stall = (float)fieldInList.GetValue(FARmodule);
                                break;
                            }
                        }
                    }
                    return genFractColor(1f-stall);
                case (int)ViewerConstants.COLORMODE.HIDE:
                    return Color.clear;
                default:
                    return Color.white;
            }
        }

        public Color genHeatmapColor(float value)
        {
            //find the appropriate color for this specific part
            Color color = new Color(0.1f,0.1f,0.1f);
            //grey to blue to cyan to green to yellow to red
            //0    to  1   to  4   to   10   to   40   to infinity
            if (value < 1) 
            {
                color.b += value * 0.9f;
            }else if(value < 4) 
            {
                color.b = 1;
                color.g += ((value - 1) / 3) * 0.9f; 
            }else if (value < 10)
            {
                color.g = 1;
                color.b += 0.9f-(((value - 4) / 6) * 0.9f);
            }
            else if (value < 40)
            {
                color.g = 1;
                color.r += ((value - 10) / 30) * 0.9f; 
            }
            else 
            {
                color.r = 1;
                color.g += 0.9f - (1-(1/(value-40+1)))*0.9f;
            }
            return color;
        }

        public Color genFractColor(float fraction) {
            //find the appropriate color for this specific part
            Color color = Color.red;
            //red to yellow to green
            if (fraction <= 0.5f)
            {
                color.g = (float)(fraction * 2);
            }
            else
            {
                color.r = (float)((1 - fraction) * 2);
                color.g = 1f;
            }
            return color;
        }

        /// <summary>
        /// Generates a beautiful rainbow.
        /// Used for the stage color display.
        /// Colors generated have the same saturation and lightness, and an even hue spread.
        /// </summary>
        /// <param name="numberOfColors"></param>
        public static Color[] genColorGradient(int numberOfColors)
        {
            Color [] gradient = new Color[numberOfColors];
            float perStep = 4f / ((float)(numberOfColors - 1));
            //colors are generated in four intervals
            //0-1 (red is maxed, green increases)
            //1-2 (green is maxed, red recedes)
            //2-3 (green is maxed, blue increases)
            //3-4 (green recedes, blue is maxed)
            //this results in a sweet rainbow of colors.
            for (int i = 0; i < numberOfColors; i++)
            {
                Color color = new Color();
                color.a = 1f;
                float pos = ((float)i) * perStep;
                if (pos <= 1)
                {
                    color.r = 1f;
                    color.g = pos;
                    color.b = 0;
                }
                else if (pos <= 2)
                {
                    color.r = 2f - pos;
                    color.g = 1f;
                    color.b = 0;
                }
                else if (pos <= 3)
                {
                    color.r = 0;
                    color.g = 1f;
                    color.b = pos - 2f;
                }
                else if (pos <= 4)
                {
                    color.r = 0;
                    color.g = 4f - pos;
                    color.b = 1f;
                }
                gradient[i] = color;
            }
            return gradient;
        }

        public void setCustomMode(CustomModeSettings customModeSettings)
        {
            customMode = customModeSettings;
        }
    }
}
