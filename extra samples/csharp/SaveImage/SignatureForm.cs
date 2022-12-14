// Notes:
// There are three coordinate spaces to deal with that are named:
//   tablet: the raw tablet coordinate
//   screen: the tablet LCD screen
//   client: the Form window client area

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Drawing.Imaging;

namespace DemoButtons
{
  public partial class SignatureForm : Form
  {
    private wgssSTU.Tablet       m_tablet;
    private wgssSTU.ICapability  m_capability;
    private wgssSTU.IInformation m_information;

    // In order to simulate buttons, we have our own Button class that stores the bounds and event handler.
    // Using an array of these makes it easy to add or remove buttons as desired.
    private delegate void ButtonClick();
    private struct Button
    {
      public Rectangle Bounds; // in Screen coordinates
      public String Text;
      public EventHandler Click;

      public void PerformClick()
      {
        Click(this, null);
      }
    };

    private Pen m_penInk;  // cached object.
    
    // The isDown flag is used like this:
    // 0 = up
    // +ve = down, pressed on button number
    // -1 = down, inking
    // -2 = down, ignoring
    private int m_isDown;

    private List<wgssSTU.IPenData> m_penData; // Array of data being stored. This can be subsequently used as desired.

    private Button[] m_btns; // The array of buttons that we are emulating.

    private Bitmap m_bitmap; // This bitmap that we display on the screen.
    private wgssSTU.encodingMode m_encodingMode; // How we send the bitmap to the device.
    private byte[] m_bitmapData; // This is the flattened data of the bitmap that we send to the device.

    // As per the file comment, there are three coordinate systems to deal with.
    // To help understand, we have left the calculations in place rather than optimise them.


    private PointF tabletToClient(wgssSTU.IPenData penData)
    {
      // Client means the Windows Form coordinates.
      return new PointF((float)penData.x * this.ClientSize.Width / m_capability.tabletMaxX, (float)penData.y * this.ClientSize.Height / m_capability.tabletMaxY);
    }



    private Point tabletToScreen(wgssSTU.IPenData penData)
    {
      // Screen means LCD screen of the tablet.
      return Point.Round(new PointF((float)penData.x * m_capability.screenWidth / m_capability.tabletMaxX, (float)penData.y * m_capability.screenHeight / m_capability.tabletMaxY));
    }
    

    
    private Point clientToScreen(Point pt)
    {
      // client (window) coordinates to LCD screen coordinates. 
      // This is needed for converting mouse coordinates into LCD bitmap coordinates as that's
      // what this application uses as the coordinate space for buttons.
      return Point.Round(new PointF((float)pt.X * m_capability.screenWidth / this.ClientSize.Width, (float)pt.Y * m_capability.screenHeight / this.ClientSize.Height));
    }


    private void clearScreen()
    {
      // note: There is no need to clear the tablet screen prior to writing an image.
      m_tablet.writeImage((byte)m_encodingMode, m_bitmapData);

      m_penData.Clear();
      m_isDown = 0;
      this.Invalidate();
    }


    private void btnJpeg_Click(object sender, EventArgs e)
    {
        // Save the image.
        SaveImage("JPEG");
        this.Close();
    }

    private void btnPng_Click(object sender, EventArgs e)
    {
        // Save the image.
        SaveImage("PNG");
        this.Close();
    }

    private void btnBmp_Click(object sender, EventArgs e)
    {
        // Save the image.
        SaveImage("BMP");
        this.Close();
    }

    private void btnGif_Click(object sender, EventArgs e)
    {
        // Save the image.
        SaveImage("GIF");
        this.Close();
    }


    private void btnCancel_Click(object sender, EventArgs e)
    {
      // You probably want to add additional processing here.
      this.m_penData = null;
      this.Close();
    }

    
    private void btnClear_Click(object sender, EventArgs e)
    {
      if (m_penData.Count != 0)
      {
        clearScreen();
      }
    }

    


    // Pass in the device you want to connect to!
    public SignatureForm(wgssSTU.IUsbDevice usbDevice)
    {
      // This is a DPI aware application, so ensure you understand how .NET client coordinates work.
      // Testing using a Windows installation set to a high DPI is recommended to understand how
      // values get scaled or not.

      this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
      this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;

      InitializeComponent();

      m_penData = new List<wgssSTU.IPenData>();

      m_tablet = new wgssSTU.Tablet();
      wgssSTU.ProtocolHelper protocolHelper = new wgssSTU.ProtocolHelper();

      // A more sophisticated applications should cycle for a few times as the connection may only be
      // temporarily unavailable for a second or so. 
      // For example, if a background process such as Wacom STU Display
      // is running, this periodically updates a slideshow of images to the device.

      wgssSTU.IErrorCode ec = m_tablet.usbConnect(usbDevice, true);
      if (ec.value == 0)
      {
        m_capability = m_tablet.getCapability();
        m_information = m_tablet.getInformation();
      }
      else
      {
        throw new Exception(ec.message);
      }

      this.SuspendLayout();
      this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
      this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;

      // Set the size of the client window to be actual size, 
      // based on the reported DPI of the monitor.

      Size clientSize = new Size((int)(m_capability.tabletMaxX / 2540F * 96F), (int)(m_capability.tabletMaxY / 2540F * 96F));
      this.ClientSize = clientSize;
      this.ResumeLayout();

      m_btns = new Button[6];
    
	  // Place the buttons across the bottom of the screen.

	  int btnWidth = m_capability.screenWidth / m_btns.Length;
      int y = m_capability.screenHeight * 6 / 7;
      int h = m_capability.screenHeight - y;
		
	  for (int j = 0; j < m_btns.Length; j++)
	  {
		if (j == 0) {
          m_btns[0].Bounds = new Rectangle(0, y, btnWidth, h);
		}
		else  {
		   m_btns[j].Bounds = new Rectangle(btnWidth * j, y, btnWidth, h);
		}
      }
     
      m_btns[0].Text = "JPEG";
      m_btns[1].Text = "PNG";
      m_btns[2].Text = "BMP";
      m_btns[3].Text = "GIF";
      m_btns[4].Text = "Clear";
      m_btns[5].Text = "Canc";

      m_btns[0].Click = new EventHandler(btnJpeg_Click);
      m_btns[1].Click = new EventHandler(btnPng_Click);
      m_btns[2].Click = new EventHandler(btnBmp_Click);
      m_btns[3].Click = new EventHandler(btnGif_Click);
      m_btns[4].Click = new EventHandler(btnClear_Click);
      m_btns[5].Click = new EventHandler(btnCancel_Click);


      // Disable color if the STU-520 bulk driver isn't installed.
      // This isn't necessary, but uploading colour images with out the driver
      // is very slow.

      // Calculate the encodingMode that will be used to update the image
      ushort idP = m_tablet.getProductId();
      wgssSTU.encodingFlag encodingFlag = (wgssSTU.encodingFlag)protocolHelper.simulateEncodingFlag(idP, 0);
      bool useColor = false;
      if ((encodingFlag & (wgssSTU.encodingFlag.EncodingFlag_16bit | wgssSTU.encodingFlag.EncodingFlag_24bit)) != 0)
      {
          if (m_tablet.supportsWrite())
              useColor = true;
      }
      if ((encodingFlag & wgssSTU.encodingFlag.EncodingFlag_24bit) != 0)
      {
          m_encodingMode = m_tablet.supportsWrite() ? wgssSTU.encodingMode.EncodingMode_24bit_Bulk : wgssSTU.encodingMode.EncodingMode_24bit;
      }
      else if ((encodingFlag & wgssSTU.encodingFlag.EncodingFlag_16bit) != 0)
      {
          m_encodingMode = m_tablet.supportsWrite() ? wgssSTU.encodingMode.EncodingMode_16bit_Bulk : wgssSTU.encodingMode.EncodingMode_16bit;
      }
      else
      {
          // assumes 1bit is available
          m_encodingMode = wgssSTU.encodingMode.EncodingMode_1bit;
      }

      // Size the bitmap to the size of the LCD screen.
      // This application uses the same bitmap for both the screen and client (window).
      // However, at high DPI, this bitmap will be stretch and it would be better to 
      // create individual bitmaps for screen and client at native resolutions.
      m_bitmap = new Bitmap(m_capability.screenWidth, m_capability.screenHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
      {
        Graphics gfx = Graphics.FromImage(m_bitmap);
        gfx.Clear(Color.White);

        // Uses pixels for units as DPI won't be accurate for tablet LCD.
        Font font = new Font(FontFamily.GenericSansSerif, m_btns[0].Bounds.Height / 2F, GraphicsUnit.Pixel);
        StringFormat sf = new StringFormat();
        sf.Alignment = StringAlignment.Center;
        sf.LineAlignment = StringAlignment.Center;

        if (useColor)
        {
          gfx.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        }
        else
        {
          // Anti-aliasing should be turned off for monochrome devices.
          gfx.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixel;
        }

        // Draw the buttons
        for (int i = 0; i < m_btns.Length; ++i)
        {
          if (useColor)
          {
            gfx.FillRectangle(Brushes.LightGray, m_btns[i].Bounds);
          }
          gfx.DrawRectangle(Pens.Black, m_btns[i].Bounds);
          gfx.DrawString(m_btns[i].Text, font, Brushes.Black, m_btns[i].Bounds, sf);
        }

        gfx.Dispose();
        font.Dispose();

        // Finally, use this bitmap for the window background.
        this.BackgroundImage = m_bitmap;
        this.BackgroundImageLayout = ImageLayout.Stretch;
      }

      // Now the bitmap has been created, it needs to be converted to device-native
      // format.
      {

        // Unfortunately it is not possible for the native COM component to
        // understand .NET bitmaps. We have therefore convert the .NET bitmap
        // into a memory blob that will be understood by COM.

        System.IO.MemoryStream stream = new System.IO.MemoryStream();
        m_bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        m_bitmapData = (byte[])protocolHelper.resizeAndFlatten(stream.ToArray(), 0, 0, (uint)m_bitmap.Width, (uint)m_bitmap.Height, m_capability.screenWidth, m_capability.screenHeight, (byte)m_encodingMode, wgssSTU.Scale.Scale_Fit, 0, 0);
        protocolHelper = null;
        stream.Dispose();
      }

      // If you wish to further optimize image transfer, you can compress the image using 
      // the Zlib algorithm.
      
      bool useZlibCompression = false;
      if (!useColor && useZlibCompression)
      {
        // m_bitmapData = compress_using_zlib(m_bitmapData); // insert compression here!
        m_encodingMode |= wgssSTU.encodingMode.EncodingMode_Zlib;
      }

      // Calculate the size and cache the inking pen.
      
      SizeF s = this.AutoScaleDimensions;
      float inkWidthMM = 0.7F;
      m_penInk = new Pen(Color.DarkBlue, inkWidthMM / 25.4F * ((s.Width + s.Height) / 2F));
      m_penInk.StartCap = m_penInk.EndCap = System.Drawing.Drawing2D.LineCap.Round;
      m_penInk.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;

      
      // Add the delagate that receives pen data.
      m_tablet.onPenData += new wgssSTU.ITabletEvents2_onPenDataEventHandler(onPenData);
      m_tablet.onGetReportException += new wgssSTU.ITabletEvents2_onGetReportExceptionEventHandler(onGetReportException);


      // Initialize the screen
      clearScreen();

      // Enable the pen data on the screen (if not already)
      m_tablet.setInkingMode(0x01);
    }



    private void Form2_FormClosed(object sender, FormClosedEventArgs e)
    {
      // Ensure that you correctly disconnect from the tablet, otherwise you are 
      // likely to get errors when wanting to connect a second time.
      if (m_tablet != null)
      {
        m_tablet.onPenData -= new wgssSTU.ITabletEvents2_onPenDataEventHandler(onPenData);
        m_tablet.onGetReportException -= new wgssSTU.ITabletEvents2_onGetReportExceptionEventHandler(onGetReportException);
        m_tablet.setInkingMode(0x00);
        m_tablet.setClearScreen();
        m_tablet.disconnect();
      }

      m_penInk.Dispose();
    }

    private void onGetReportException(wgssSTU.ITabletEventsException tabletEventsException)
    {
      try
      {
        tabletEventsException.getException();
      }
      catch (Exception e)
      {
        MessageBox.Show("Error: " + e.Message);
        m_tablet.disconnect();
        m_tablet = null;
        m_penData = null;
        this.Close();
      }
    }

    private void onPenData(wgssSTU.IPenData penData) // Process incoming pen data
    {
      Point pt = tabletToScreen(penData);

      int btn = 0; // will be +ve if the pen is over a button.
      {        
        for (int i = 0; i < m_btns.Length; ++i)
        {
          if (m_btns[i].Bounds.Contains(pt))
          {
            btn = i+1;
            break;
          }          
        }
      }

      bool isDown = (penData.sw != 0);

      // This code uses a model of four states the pen can be in:
      // down or up, and whether this is the first sample of that state.

      if (isDown)
      {
        if (m_isDown == 0)
        {
          // transition to down
          if (btn > 0)
          {
            // We have put the pen down on a button.
            // Track the pen without inking on the client.

            m_isDown = btn; 
          }
          else
          {
            // We have put the pen down somewhere else.
            // Treat it as part of the signature.

            m_isDown = -1;
          }
        }
        else
        {
          // already down, keep doing what we're doing!
        }

        // draw
        if (m_penData.Count != 0 && m_isDown == -1)
        {
          // Draw a line from the previous down point to this down point.
          // This is the simplist thing you can do; a more sophisticated program
          // can perform higher quality rendering than this!
          
          Graphics gfx = this.CreateGraphics();
          gfx.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
          gfx.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.High;
          gfx.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
          gfx.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
          
          wgssSTU.IPenData prevPenData = m_penData[m_penData.Count - 1];

          PointF prev = tabletToClient(prevPenData);

          gfx.DrawLine(m_penInk, prev, tabletToClient(penData));
          gfx.Dispose();
        }

        // The pen is down, store it for use later.
        if (m_isDown == -1)
          m_penData.Add(penData);
      }
      else
      {
        if (m_isDown != 0)
        {
          // transition to up
          if (btn > 0)
          {
            // The pen is over a button

            if (btn == m_isDown)
            {
              // The pen was pressed down over the same button as is was lifted now. 
              // Consider that as a click!
              m_btns[btn - 1].PerformClick();
            }
          }
          m_isDown = 0;
        }
        else
        {
           // still up
        }

        // Add up data once we have collected some down data.
        if (m_penData != null)
        {
            if (m_penData.Count != 0)
                m_penData.Add(penData);
        }
      }
    }

       


    private void Form2_Paint(object sender, PaintEventArgs e)
    {
      if (m_penData.Count != 0)
      {
        // Redraw all the pen data up until now!

        Graphics gfx = e.Graphics;
        gfx.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
        gfx.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.High;
        gfx.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        gfx.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
        bool isDown = false;
        PointF prev = new PointF();
        for (int i = 0; i < m_penData.Count; ++i)
        {
          if (m_penData[i].sw != 0)
          {
            if (!isDown)
            {
              isDown = true;
              prev = tabletToClient(m_penData[i]);
            }
            else
            {
              PointF curr = tabletToClient(m_penData[i]);
              gfx.DrawLine(m_penInk, prev, curr);
              prev = curr;
            }
          }
          else
          {
            if (isDown)
            {
              isDown = false;
            }
          }
        }
      }
          
    }

    private void Form2_MouseClick(object sender, MouseEventArgs e)
    {      
      // Enable the mouse to click on the simulated buttons that we have displayed.
      
      // Note that this can add some tricky logic into processing pen data
      // if the pen was down at the time of this click, especially if the pen was logically
      // also 'pressing' a button! This demo however ignores any that.

      Point pt = clientToScreen(e.Location);
      foreach (Button btn in m_btns)
      {
        if (btn.Bounds.Contains(pt))
        {
          btn.PerformClick();
          break;
        }
      }
    }



    public List<wgssSTU.IPenData> getPenData()
    {
      return m_penData;
    }

    public wgssSTU.ICapability getCapability()
    {
      return m_penData != null ? m_capability : null;
    }

    public wgssSTU.IInformation getInformation()
    {
      return m_penData != null ? m_information : null;
    }

    // Save the image in a local file
    private void SaveImage(String ImageType)
    {
        String fileExtension;
        String saveLocation;

        try
        {
            Bitmap bitmap = GetImage(new Rectangle(0, 0, m_capability.screenWidth, m_capability.screenHeight));

            switch (ImageType)
            {
                case "JPEG":
                    fileExtension = "jpg";
                    saveLocation = System.Environment.CurrentDirectory + "\\" + "signature_output." + fileExtension;
                    bitmap.Save(saveLocation, ImageFormat.Jpeg);
                    break;
                case "PNG":
                    fileExtension = "png";
                    saveLocation = System.Environment.CurrentDirectory + "\\" + "signature_output." + fileExtension;
                    bitmap.Save(saveLocation, ImageFormat.Png);
                    break;
                case "BMP":
                    fileExtension = "bmp";
                    saveLocation = System.Environment.CurrentDirectory + "\\" + "signature_output." + fileExtension;
                    bitmap.Save(saveLocation, ImageFormat.Bmp);
                    break;
                case "GIF":
                    fileExtension = "gif";
                    saveLocation = System.Environment.CurrentDirectory + "\\" + "signature_output." + fileExtension;
                    bitmap.Save(saveLocation, ImageFormat.Gif);
                    break;
                default:
                    fileExtension = "jpg";
                    saveLocation = System.Environment.CurrentDirectory + "\\" + "signature_output." + fileExtension;
                    bitmap.Save(saveLocation, ImageFormat.Jpeg);
                    break;
            }       
            //MessageBox.Show("Saved to" + saveLocation);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Exception: " + ex.Message);
        }
    }

    // Draw an image with the existed points.
    public Bitmap GetImage(Rectangle rect)
    {
        Bitmap retVal = null;
        Bitmap bitmap = null;
        SolidBrush brush = null;
        try
        {
            bitmap = new Bitmap(rect.Width, rect.Height);
            Graphics graphics = Graphics.FromImage(bitmap);

            graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.High;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

            brush = new SolidBrush(Color.White);
            graphics.FillRectangle(brush, 0, 0, rect.Width, rect.Height);

            for (int i = 1; i < m_penData.Count; i++)
            {
                PointF p1 = tabletToScreen(m_penData[i - 1]);
                PointF p2 = tabletToScreen(m_penData[i]);

                if (m_penData[i - 1].sw > 0 || m_penData[i].sw > 0)
                {
                    graphics.DrawLine(m_penInk, p1, p2);
                }
            }

            retVal = bitmap;
            bitmap = null;
        }
        finally
        {
            if (brush != null)
                brush.Dispose();
            if (bitmap != null)
                bitmap.Dispose();
        }
        return retVal;
    }
      
  }
}
