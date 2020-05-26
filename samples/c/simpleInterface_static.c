/* TODO: Add comments! */

#include <WacomGSS/wgssSTU.h>

#include <stdio.h>
#include <signal.h>

#define WacomGSS_unused_parameter(X) { (void)(X); }

static volatile int       g_quitFlag;
static WacomGSS_Interface g_intf;








static void signalHandler(int i)
{
  g_quitFlag = 1;
  /*int e =*/ WacomGSS_Interface_queueNotifyAll(g_intf); /* notify the interface that the predicate has changed */

  WacomGSS_unused_parameter(i)
}



static WacomGSS_bool WacomGSS_DECL quitSet(void * p)
{
  WacomGSS_unused_parameter(p)
  return g_quitFlag;
}



static int displayError(int e)
{
  if (e)
  {
    char * message;
    int code;
    int e2 = WacomGSS_getException(&code, NULL, &message);
    if (!e2)
    {
      printf("error %d: %d %s\n", e, code, message);
      WacomGSS_free(message);
    }
    else
    {
      printf("error %d - getException() failed %d\n", e, e2);
    }
  }
  return e;
}



static void display(uint8_t const * begin, uint8_t const * end)
{
  while (begin != end)
  {
    printf(" %02x", *begin++);
  }
  printf("\n");
}



static int WacomGSS_DECL onPenData(void * reportHandler, size_t sizeofPenData, WacomGSS_PenData const * penData)
{
  WacomGSS_unused_parameter(reportHandler)
  if (sizeofPenData == sizeof(WacomGSS_PenData))
  {
    printf("%1u %1u %3u %5u %5u\n", penData->rdy, penData->sw, penData->pressure, penData->x, penData->y);
    return 0;
  }
  return 1;
}



static int WacomGSS_DECL onPenDataOption(void * reportHandler, size_t sizeofPenDataOption, WacomGSS_PenDataOption const * penData)
{
  WacomGSS_unused_parameter(reportHandler)
  if (sizeofPenDataOption == sizeof(WacomGSS_PenDataOption))
  {
    printf("%1u %1u %3u %5u %5u [%5u]\n", penData->rdy, penData->sw, penData->pressure, penData->x, penData->y, penData->option);
    return 0;
  }
  return 1;
}



static int WacomGSS_DECL onPenDataEncrypted(void * reportHandler, size_t sizeofPenDataEncrypted, WacomGSS_PenDataEncrypted const * penData)
{
  WacomGSS_unused_parameter(reportHandler)
  if (sizeofPenDataEncrypted == sizeof(WacomGSS_PenDataEncrypted))
  {
    printf("<%08x> %1u %1u %3u %5u %5u\n"
       "           %1u %1u %3u %5u %5u\n",
           penData->sessionId,
           penData->penData[0].rdy, penData->penData[0].sw, penData->penData[0].pressure, penData->penData[0].x, penData->penData[0].y,
           penData->penData[1].rdy, penData->penData[1].sw, penData->penData[1].pressure, penData->penData[1].x, penData->penData[1].y);
    return 0;
  }
  return 1;
}



static int WacomGSS_DECL onPenDataEncryptedOption(void * reportHandler, size_t sizeofPenDataEncryptedOption, WacomGSS_PenDataEncryptedOption const * penData)
{
  WacomGSS_unused_parameter(reportHandler)
  if (sizeofPenDataEncryptedOption == sizeof(WacomGSS_PenDataEncryptedOption))
  {
    printf("<%08x> %1u %1u %3u %5u %5u [%5u]\n"
       "           %1u %1u %3u %5u %5u [%5u]\n",
           penData->sessionId,
           penData->penData[0].rdy, penData->penData[0].sw, penData->penData[0].pressure, penData->penData[0].x, penData->penData[0].y, penData->option[0],
           penData->penData[1].rdy, penData->penData[1].sw, penData->penData[1].pressure, penData->penData[1].x, penData->penData[1].y, penData->option[1]);
    return 0;
  }
  return 1;
}



static void run(WacomGSS_Interface handle)
{
  int e; 
  WacomGSS_InterfaceQueue interfaceQueue = NULL;
  WacomGSS_ReportHandlerFunctionTable reportHandlerFunctionTable = { onPenData, onPenDataOption, onPenDataEncrypted, onPenDataEncryptedOption, NULL, NULL };
  uint8_t penDataOptionMode;

  e = WacomGSS_Protocol_getPenDataOptionMode(handle, &penDataOptionMode);
  if (e == 0)
  {
    printf("penDataOptionMode = %u\n", penDataOptionMode);
  }
  
  printf("setClearScreen()... ");
  e = WacomGSS_Protocol_setClearScreen(handle);
  if (displayError(e)) return;
  printf("Ok!\n");

  e = WacomGSS_Interface_interfaceQueue(handle, &interfaceQueue);
  if (displayError(e)) return;

  for (;;)
  {
    WacomGSS_bool ret;
    uint8_t * report;
    size_t    length;

    e = WacomGSS_InterfaceQueue_wait_getReportPredicate(interfaceQueue, NULL, &quitSet, &report, &length, &ret);
    if (displayError(e)) break;

    if (ret)
    {
      uint8_t const * ptr;
      e = WacomGSS_ReportHandler_handleReport(sizeof(reportHandlerFunctionTable), &reportHandlerFunctionTable, NULL, report, length, &ptr, &ret);
      if (displayError(e) == 0)
      {
        if (ptr != report+length)
        {
          if (ret)
          {
            printf("unknown data in report:");
          }
          else
          {
            printf("pending data in report:");
          }
          display(ptr, report+length);
        }
      }

      WacomGSS_free(report);
    }
    else
    {
      // quitSet
      break;
    }
  }

  if (e == 0)
  {
    printf("setClearScreen()... ");
    e = WacomGSS_Protocol_setClearScreen(handle);
    displayError(e);
  }

  e = WacomGSS_InterfaceQueue_free(interfaceQueue);
  displayError(e);

  e = WacomGSS_Interface_disconnect(handle);
  displayError(e);
}



int main(void)
{


  {
    WacomGSS_UsbDevice * usbDevices;
    size_t count;
    int e = WacomGSS_getUsbDevices(sizeof(WacomGSS_UsbDevice), &count, &usbDevices);
    if (!e)
    {
      if (count)
      { 
        WacomGSS_Interface intf;

        printf("Connecting %04x:%04x:%04x...\n", usbDevices[0].usbDevice.idVendor, usbDevices[0].usbDevice.idProduct, usbDevices[0].usbDevice.bcdDevice);
        e = WacomGSS_UsbInterface_create_1(sizeof(WacomGSS_UsbDevice), &usbDevices[0], WacomGSS_true, &intf);

        if (e == 0)
        {
          printf("Connected!\n");

          g_intf = intf;
          signal(SIGINT, &signalHandler);
    
          run(intf);
    
          signal(SIGINT, SIG_DFL);    
          g_intf = NULL;

          WacomGSS_Interface_free(intf);
        }
        else
        {
          printf("WacomGSS_UsbInterface_create() ");
          displayError(e);
        }
      }
      else
      {
        printf("no devices found!\n");
      }
    }
    else
    {
      printf("WacomGSS_getUsbDevices() ");
      displayError(e);
    }


  }

  return 0;
}
