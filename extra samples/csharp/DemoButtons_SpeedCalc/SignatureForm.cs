
/*
 SignatureForm.cs

 Allows user to input a signature on an STU and reproduces it on a Window on the PC.
 Signature can also be saved to disk as a JPEG image

 Copyright (c) 2015 Wacom GmbH. All rights reserved.

*/
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
using System.IO;
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
      public bool ClickPerformed;

      public void PerformClick()
      {
          if (this.ClickPerformed == false)
          {
              Click(this, null);
              this.ClickPerformed = true;
          }
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
    private List<wgssSTU.IPenDataTimeCountSequence> m_penTimeData; // Array of data being stored. This can be subsequently used as desired.

    private Button[] m_btns; // The array of buttons that we are emulating.

    private Bitmap m_bitmap; // This bitmap that we display on the screen.
    private wgssSTU.encodingMode m_encodingMode; // How we send the bitmap to the device.
    private byte[] m_bitmapData; // This is the flattened data of the bitmap that we send to the device.

    private int clickEventCount = 0;  // Counts the number of times the OK button click event has been called

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
      m_penTimeData.Clear();
      m_isDown = 0;
      this.Invalidate();
    }


    private void btnOk_Click(object sender, EventArgs e)
    {
        ++clickEventCount;
        if (clickEventCount == 1)
        {
            // Save the image.
            SaveImage();
            //  Calculate the average speed of the pen
            calcSpeed();
            this.Close();
        }
    }

    
    private void btnCancel_Click(object sender, EventArgs e)
    {
      // You probably want to add additional processing here.
      this.m_penData = null;
      this.m_penTimeData = null;
      this.Close();
    }

    
    private void btnClear_Click(object sender, EventArgs e)
    {
      if (m_penData.Count != 0 || m_penTimeData.Count != 0)
      {
        clearScreen();
      }
    }

    // Count no of pixels traversed between 2 co-ordinates
    private int countPixels(UInt16 lastX, UInt16 lastY, UInt16 currX, UInt16 currY)
    {
        int pixelDistance = 0;
        int tempCurrX = 0;
        int tempCurrY = 0;
        int tempLastX = 0;
        int tempLastY = 0;

        int tempX = 0;
        int tempY = 0;
        int tempZ = 0;
        string logText;

        // Possibilities are straight line along x co-ordinate, straight line along y co-ordinate or diagonal

        if (lastX == currX)
        {
            if (lastY != currY)
            {
                // Only y co-ordinate has changed
                if (currY > lastY)
                {
                    pixelDistance = currY - lastY;
                }
                else
                    pixelDistance = lastY - currY;
            }
            else
            {
                // No change in either co-ordinate
                pixelDistance = 0;
            }
        }
        else
        {
            // x has changed - what about y?
            if (lastY == currY)
            {
                // y has not changed so just use the difference in the x co-ordinate
                if (currX > lastX)
                {
                    pixelDistance = currX - lastX;
                }
                else
                    pixelDistance = lastX - currX;
            }
            else
            {
                // Both x and y have changed so we have to calculate the length of a diagonal line between the 2
                // Convert x and y to positive values and then use standard Pythagorean theorem calculation for the diagonal
                tempLastX = Math.Abs(lastX);
                tempCurrX = Math.Abs(currX);
                tempLastY = Math.Abs(lastY);
                tempCurrY = Math.Abs(currY);

                // We just want a positive difference value to calculate the diagonal distance (3rd side of the triangle)
                tempX = Math.Abs(tempLastX - tempCurrX);
                tempY = Math.Abs(tempLastY - tempCurrY);

                tempZ = (tempX * tempX) + (tempY * tempY);
                pixelDistance = (int)Math.Round(Math.Sqrt(tempZ), 0);
            }
        }
        logText = "Distance from " + lastX + "/" + lastY + " to " + currX + "/" + currY + " = " + pixelDistance;
        logFile(logText);

        return pixelDistance;
    }
   
    public void logFile(string logMessage)
    {
       StreamWriter log;

       if (!File.Exists("logfile.txt"))
       {
           log = new StreamWriter("logfile.txt");
       }
       else
       {
           log = File.AppendText("logfile.txt");
       }
       // Write to the file:
       log.WriteLine("Date/Time:" + DateTime.Now);
       log.WriteLine(logMessage);
       // Close the stream:
       log.Close();
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

      m_penTimeData = new List<wgssSTU.IPenDataTimeCountSequence>();
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

        // Set up the tablet to return time stamp with the pen data
        m_tablet.setPenDataOptionMode((byte)wgssSTU.penDataOptionMode.PenDataOptionMode_TimeCountSequence);
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

      m_btns = new Button[3];
      if (usbDevice.idProduct != 0x00a2)
      {
        // Place the buttons across the bottom of the screen.

        int w2 = m_capability.screenWidth / 3;
        int w3 = m_capability.screenWidth / 3;
        int w1 = m_capability.screenWidth - w2 - w3;
        int y = m_capability.screenHeight * 6 / 7;
        int h = m_capability.screenHeight - y;

        m_btns[0].Bounds = new Rectangle(0, y, w1, h);
        m_btns[1].Bounds = new Rectangle(w1, y, w2, h);
        m_btns[2].Bounds = new Rectangle(w1 + w2, y, w3, h);
      }
      else
      {
        // The STU-300 is very shallow, so it is better to utilise
        // the buttons to the side of the display instead.

        int x = m_capability.screenWidth * 3 / 4;
        int w = m_capability.screenWidth - x;

        int h2 = m_capability.screenHeight / 3;
        int h3 = m_capability.screenHeight / 3;
        int h1 = m_capability.screenHeight - h2 - h3;

        m_btns[0].Bounds = new Rectangle(x, 0, w, h1);
        m_btns[1].Bounds = new Rectangle(x, h1, w, h2);
        m_btns[2].Bounds = new Rectangle(x, h1 + h2, w, h3);
      }
      m_btns[0].Text = "OK";
      m_btns[1].Text = "Clear";
      m_btns[2].Text = "Cancel";
      m_btns[0].Click = new EventHandler(btnOk_Click);
      m_btns[1].Click = new EventHandler(btnClear_Click);
      m_btns[2].Click = new EventHandler(btnCancel_Click);


      m_btns[0].ClickPerformed = false;
      m_btns[1].ClickPerformed = false;
      m_btns[2].ClickPerformed = false;

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

      
      // Add the standard delegate that receives pen data.
      m_tablet.onPenData += new wgssSTU.ITabletEvents2_onPenDataEventHandler(onPenData);
      m_tablet.onGetReportException += new wgssSTU.ITabletEvents2_onGetReportExceptionEventHandler(onGetReportException);

      // Add the delegate for receiving the time stamp data
      m_tablet.onPenDataTimeCountSequence += new wgssSTU.ITabletEvents2_onPenDataTimeCountSequenceEventHandler(onPenDataTimeCountSequence);

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
        m_penTimeData = null;
        this.Close();
      }
    }

    private void onPenDataTimeCountSequence(wgssSTU.IPenDataTimeCountSequence penTimeData)
    {
        UInt16 penSequence;
        UInt16 penTimeStamp;
        UInt16 penPressure;
        UInt16 x;
        UInt16 y;

        penPressure = penTimeData.pressure;
        penTimeStamp = penTimeData.timeCount;
        penSequence = penTimeData.sequence;
        x = penTimeData.x;
        y = penTimeData.y;

        logFile("Time/seq/x/y/pressure " + penTimeStamp + " " + penSequence + " " + x + " " + y + " " + penPressure);
        Point pt = tabletToScreen(penTimeData);

        int btn = 0; // will be +ve if the pen is over a button.
        {
            for (int i = 0; i < m_btns.Length; ++i)
            {
                if (m_btns[i].Bounds.Contains(pt))
                {
                    btn = i + 1;
                    break;
                }
            }
        }

        bool isDown = (penTimeData.sw != 0);

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
            if (m_penTimeData.Count != 0 && m_isDown == -1)
            {
                // Draw a line from the previous down point to this down point.
                // This is the simplist thing you can do; a more sophisticated program
                // can perform higher quality rendering than this!

                Graphics gfx = this.CreateGraphics();
                gfx.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                gfx.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.High;
                gfx.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                gfx.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

                wgssSTU.IPenData prevPenData = m_penTimeData[m_penTimeData.Count - 1];

                PointF prev = tabletToClient(prevPenData);

                gfx.DrawLine(m_penInk, prev, tabletToClient(penTimeData));
                gfx.Dispose();
            }

            // The pen is down, store it for use later.
            if (m_isDown == -1)
                m_penTimeData.Add(penTimeData);
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
            if (m_penTimeData.Count != 0)
                m_penTimeData.Add(penTimeData);
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
        if (m_penData.Count != 0)
          m_penData.Add(penData);
      }
    }

    private void Form2_Paint(object sender, PaintEventArgs e)
    {
      if (m_penTimeData.Count != 0)
      {
        // Redraw all the pen data up until now!

        Graphics gfx = e.Graphics;
        gfx.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
        gfx.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.High;
        gfx.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        gfx.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
        bool isDown = false;
        PointF prev = new PointF();
        for (int i = 0; i < m_penTimeData.Count; ++i)
        {
          if (m_penTimeData[i].sw != 0)
          {
            if (!isDown)
            {
              isDown = true;
              prev = tabletToClient(m_penTimeData[i]);
            }
            else
            {
              PointF curr = tabletToClient(m_penTimeData[i]);
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
      
    public List<wgssSTU.IPenDataTimeCountSequence> getPenData()
    {
      return m_penTimeData;
    }

    public wgssSTU.ICapability getCapability()
    {
      return m_penTimeData != null ? m_capability : null;
    }

    public wgssSTU.IInformation getInformation()
    {
      return m_penTimeData != null ? m_information : null;
    }

    // Save the image in a local file
    private void SaveImage()
    {
        try
        {
            Bitmap bitmap = GetImage(new Rectangle(0, 0, m_capability.screenWidth, m_capability.screenHeight));
            string saveLocation = System.Environment.CurrentDirectory + "\\" + "signature_output.jpg";
            bitmap.Save(saveLocation, ImageFormat.Jpeg);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Exception: " + ex.Message);
        }
    }

    // Calculate average speed of the pen while creating the signature
    private void calcSpeed()
    {
        int i;
        int totalPixels = 0;
        UInt16 endTime, startTime;
        UInt16 lastX, lastY;
        decimal averageSpeed;
        decimal roundedSpeed;
        float kmPerHour;
        float metresPerHour;
        float metresPerSecond;
        float mmPerSecond;
        float penPointsPerSecond;
        float roundedPixelSpeed;
        String logText;

        // Store the start and end time of the pen data input
        startTime = m_penTimeData[0].timeCount;
        endTime = m_penTimeData[m_penTimeData.Count - 1].timeCount;

        lastX = m_penTimeData[0].x;
        lastY = m_penTimeData[0].y;

        // Count the number of pixels traversed by the pen by using the data in the array
        for (i = 0; i < m_penTimeData.Count; i++)
        {
            // If both co-ordinates are zero for either pair then no calculation can be made
            if ((lastX > 0 || lastY > 0) && (m_penTimeData[i].x > 0 || m_penTimeData[i].y > 0))
            {
                totalPixels += countPixels(lastX, lastY, m_penTimeData[i].x, m_penTimeData[i].y);
                lastX = m_penTimeData[i].x;
                lastY = m_penTimeData[i].y;
            }
        }
        averageSpeed = (decimal)totalPixels / (endTime - startTime);
        roundedSpeed = Math.Round(averageSpeed, 2);

        logText = "Time lapse from " + startTime + " to " + endTime + ": " + (endTime - startTime) + ". Pixels: " + totalPixels + ". Average pen points per ms: " + roundedSpeed.ToString();
        logFile(logText);

        // The 530 is 800 x 480 pixels but the # of pen points is 10800 x 6480 which is a ratio of 13.5 pen points to 1 pixel
        // Therefore the average speed in pixels is lower
        roundedPixelSpeed = (float)roundedSpeed;
        roundedPixelSpeed /= 13.5f;
        logText = "Average pixel speed per ms = " + roundedPixelSpeed;
        logFile(logText);

        // There are 100 pen points per millimeter (the pad measures 10.8 cm x 6.48cm) so we can now calculate metres per second and km per hour
        // First multiply the average no of pen points by 1000 to get the number per second
        penPointsPerSecond = (float)(roundedSpeed * 1000);

        // Divide by 100 to get the number of millimetres covered per second, then by 1000 to get the number of metres
        mmPerSecond = penPointsPerSecond / 100;
        metresPerSecond = mmPerSecond / 1000;

        // Multiple by 3600 to get the number of metres per hour
        metresPerHour = metresPerSecond * 3600;
        // Finally divide by 1000 to get the number of km per hour
        kmPerHour = metresPerHour / 1000;

        logText = "Metres per second: " + metresPerSecond.ToString() + ".  Km per hour: " + kmPerHour.ToString();
        logFile(logText);

        if (clickEventCount == 1)
        {
            logText = "Average pen points per ms: " + roundedSpeed.ToString() + "\r\nPixel speed per ms: " + roundedPixelSpeed + "\r\nMetres/sec: " + metresPerSecond.ToString() + "\r\nKm/h: " + kmPerHour.ToString();
            MessageBox.Show(logText);
        }
    }

    // Draw an image with the existing points.
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

            for (int i = 1; i < m_penTimeData.Count; i++)
            {
                PointF p1 = tabletToScreen(m_penTimeData[i - 1]);
                PointF p2 = tabletToScreen(m_penTimeData[i]);

                if (m_penTimeData[i - 1].sw > 0 || m_penTimeData[i].sw > 0)
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
