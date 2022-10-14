using Microsoft.Xna.Framework;

namespace JPLOPS.Imaging
{
    public class PDSCameraModelParser
    {
        const int CAHVORE_CMOD_TYPE_PERSPECTIVE = 1;
        const int CAHVORE_CMOD_TYPE_FISHEYE = 2;
        const int CAHVORE_CMOD_TYPE_GENERAL = 3;

        PDSMetadata metadata;
        public PDSCameraModelParser(PDSMetadata metadata)
        {
            this.metadata = metadata;
        }

        public CameraModel Parse()
        {
            // Read camera model type
            string cameraModelType = null;
            if (metadata.HasGroup("GEOMETRIC_CAMERA_MODEL"))
            {
                cameraModelType = metadata.ReadAsString("GEOMETRIC_CAMERA_MODEL", "MODEL_TYPE");
            }
            else if (metadata.HasGroup("GEOMETRIC_CAMERA_MODEL_PARMS"))
            {
                cameraModelType = metadata.ReadAsString("GEOMETRIC_CAMERA_MODEL_PARMS", "MODEL_TYPE");
            }
            if (cameraModelType == "CAHV")
            {
                return new CAHV(CAHV_C, CAHV_A, CAHV_H, CAHV_V);
            }
            else if (cameraModelType == "CAHVOR")
            {
                return new CAHVOR(CAHV_C, CAHV_A, CAHV_H, CAHV_V, CAHV_O, CAHV_R);
            }
            else if (cameraModelType == "CAHVORE")
            {
                bool validMode = false;
                double linearity = CAHVORE.PERSPECTIVE_LINEARITY;
                if (CAHVORE_MType == CAHVORE_CMOD_TYPE_PERSPECTIVE)
                {
                    linearity = CAHVORE.PERSPECTIVE_LINEARITY;
                    validMode = true;
                }
                else if (CAHVORE_MType == CAHVORE_CMOD_TYPE_FISHEYE)
                {
                    linearity = CAHVORE.FISHEYE_LINEARITY;
                    validMode = true;
                }
                else if (CAHVORE_MType == CAHVORE_CMOD_TYPE_GENERAL)
                {
                    linearity = CAHVORE_MParm;
                    validMode = true;
                }
                if (validMode)
                {
                    return new CAHVORE(CAHV_C, CAHV_A, CAHV_H, CAHV_V, CAHV_O, CAHV_R, CAHV_E, linearity);
                }
            }

            return null;
        }

        Vector2 Ortho_P
        {
            get { return ReadOrthoComponent(1); }
        }
        Vector2 Ortho_S
        {
            get { return ReadOrthoComponent(2); }
        }
        Vector3 CAHV_C
        {
            get { return ReadCAHVORComponent(1); }
        }
        Vector3 CAHV_A
        {
            get { return ReadCAHVORComponent(2); }
        }
        Vector3 CAHV_H
        {
            get { return ReadCAHVORComponent(3); }
        }
        Vector3 CAHV_V
        {
            get { return ReadCAHVORComponent(4); }
        }
        Vector3 CAHV_O
        {
            get { return ReadCAHVORComponent(5); }
        }
        Vector3 CAHV_R
        {
            get { return ReadCAHVORComponent(6); }
        }
        Vector3 CAHV_E
        {
            get { return ReadCAHVORComponent(7); }
        }

        double CAHVORE_MParm
        {
            get
            {
                if (metadata.HasGroup("GEOMETRIC_CAMERA_MODEL"))
                    return metadata.ReadAsDouble("GEOMETRIC_CAMERA_MODEL", "MODEL_COMPONENT_9");
                return metadata.ReadAsDouble("GEOMETRIC_CAMERA_MODEL_PARMS", "MODEL_COMPONENT_9");
            }
        }

        int CAHVORE_MType
        {
            get
            {
                if (metadata.HasGroup("GEOMETRIC_CAMERA_MODEL"))
                    return (int)metadata.ReadAsDouble("GEOMETRIC_CAMERA_MODEL", "MODEL_COMPONENT_8");
                return (int)metadata.ReadAsDouble("GEOMETRIC_CAMERA_MODEL_PARMS", "MODEL_COMPONENT_8");
            }
        }

        Vector3 ReadCAHVORComponent(int i)
        {
            string componentName = "MODEL_COMPONENT_" + i;
            if (metadata.HasGroup("GEOMETRIC_CAMERA_MODEL"))
                return new Vector3(metadata.ReadAsDoubleArray("GEOMETRIC_CAMERA_MODEL", componentName));
            return new Vector3(metadata.ReadAsDoubleArray("GEOMETRIC_CAMERA_MODEL_PARMS", componentName));
        }

        private Vector2 ReadOrthoComponent(int i)
        {
            string componentName = "MODEL_COMPONENT_" + i;
            if (metadata.HasGroup("GEOMETRIC_CAMERA_MODEL"))
                return new Vector2(metadata.ReadAsDoubleArray("GEOMETRIC_CAMERA_MODEL", componentName));
            return new Vector2(metadata.ReadAsDoubleArray("GEOMETRIC_CAMERA_MODEL_PARMS", componentName));
        }
    }
}
