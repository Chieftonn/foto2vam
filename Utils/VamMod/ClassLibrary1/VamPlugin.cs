// Foto2Vam Mod for VAM. Use dnSpy to inject this into the executable and trigger creation of Foto2VamServer.

using SimpleJSON;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using UnityEngine;
using VamMod;


namespace Foto2VamPlugins
{
  public class Foto2VamPlugin : MVRScript
  {
    Foto2VamServer server = null;

    void Start()
    {
      if (server == null)
      {
        server = new Foto2VamServer();
      }
    }

    void OnDestroy()
    {
      server.Stop();
      server = null;
    }

  }
}

