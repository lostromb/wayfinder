using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenTK.Graphics.OpenGL;

using OpenGL = OpenTK.Graphics.OpenGL;
using SDI = System.Drawing.Imaging;

namespace WayfinderUI
{
    public class GLTexture : IDisposable
    {
        private GLTexture(int handle, TextureTarget type, int w, int h)
        {
            Handle = handle;
            Type = type;
            Width = w;
            Height = h;
        }

        public int Handle { get; private set; }
        public TextureTarget Type { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }

        public float AspectRatio
        {
            get
            {
                if (Height == 0)
                {
                    return 1.0f;
                }

                return (float)Width / (float)Height;
            }
        }

        public static GLTexture Load(FileInfo file)
        {
            Image image = Image.FromFile(file.FullName);
            Bitmap bmp = new Bitmap(image);
            if (bmp.Height == 1)
            {
                return LoadImage1D(bmp);
            }
            else
            {
                return LoadImage2D(bmp);
            }
        }

        private static GLTexture LoadImage2D(Bitmap image)
        {
            int texID = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, texID);

            SDI.PixelFormat sourcePixelFormat = image.PixelFormat;
            OpenGL.PixelInternalFormat glInternalFormat = OpenGL.PixelInternalFormat.Rgba;
            OpenGL.PixelFormat glInterpretationFormat = OpenGL.PixelFormat.Bgra;
            OpenGL.PixelType glInterpretationStorage = OpenGL.PixelType.UnsignedByte;

            SDI.BitmapData data = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), SDI.ImageLockMode.ReadOnly, sourcePixelFormat);
            
            if (sourcePixelFormat == SDI.PixelFormat.Format32bppArgb)
            {
                glInternalFormat = OpenGL.PixelInternalFormat.Rgba;
                glInterpretationFormat = OpenGL.PixelFormat.Bgra;
                glInterpretationStorage = OpenGL.PixelType.UnsignedByte;
            }
            else if (sourcePixelFormat == SDI.PixelFormat.Format24bppRgb)
            {
                glInternalFormat = OpenGL.PixelInternalFormat.Rgb;
                glInterpretationFormat = OpenGL.PixelFormat.Bgr;
                glInterpretationStorage = OpenGL.PixelType.UnsignedByte;
            }

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            GL.TexImage2D(TextureTarget.Texture2D, 0, glInternalFormat, data.Width, data.Height, 0, glInterpretationFormat, glInterpretationStorage, data.Scan0);
            image.UnlockBits(data);

            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            GL.BindTexture(TextureTarget.Texture2D, 0);

            return new GLTexture(texID, TextureTarget.Texture2D, data.Width, data.Height);
        }

        private static GLTexture LoadImage1D(Bitmap image)
        {
            int texID = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture1D, texID);

            SDI.PixelFormat sourcePixelFormat = image.PixelFormat;
            OpenGL.PixelInternalFormat glInternalFormat = OpenGL.PixelInternalFormat.Rgba;
            OpenGL.PixelFormat glInterpretationFormat = OpenGL.PixelFormat.Bgra;
            OpenGL.PixelType glInterpretationStorage = OpenGL.PixelType.UnsignedByte;

            SDI.BitmapData data = image.LockBits(new Rectangle(0, 0, image.Width, 1), SDI.ImageLockMode.ReadOnly, sourcePixelFormat);

            if (sourcePixelFormat == SDI.PixelFormat.Format32bppArgb)
            {
                glInternalFormat = OpenGL.PixelInternalFormat.Rgba;
                glInterpretationFormat = OpenGL.PixelFormat.Bgra;
                glInterpretationStorage = OpenGL.PixelType.UnsignedByte;
            }
            else if (sourcePixelFormat == SDI.PixelFormat.Format24bppRgb)
            {
                glInternalFormat = OpenGL.PixelInternalFormat.Rgb;
                glInterpretationFormat = OpenGL.PixelFormat.Bgr;
                glInterpretationStorage = OpenGL.PixelType.UnsignedByte;
            }

            GL.TexImage1D(TextureTarget.Texture1D, 0, glInternalFormat, data.Width, 0, glInterpretationFormat, glInterpretationStorage, data.Scan0);
            image.UnlockBits(data);

            GL.GenerateMipmap(GenerateMipmapTarget.Texture1D);
            GL.BindTexture(TextureTarget.Texture1D, 0);

            return new GLTexture(texID, TextureTarget.Texture1D, data.Width, 1);
        }

        public void Dispose()
        {
            GL.DeleteTexture(Handle);
        }
    }
}
