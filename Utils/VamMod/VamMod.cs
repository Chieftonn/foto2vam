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


namespace VamMod
{
	public class Foto2VamServer
	{
		~Foto2VamServer()
		{
			this._exitThread = true;
			this._event.Set();
			UnityEngine.Object.Destroy(this._imageMaker);
			this._thread.Join();
		}

		private void HandleTakeScreenshot(JSONNode aJsonNode)
		{
			string value = aJsonNode["json"].Value;
			string value2 = aJsonNode["outputPath"].Value;
			int asInt = aJsonNode["dimensions"][0].AsInt;
			int asInt2 = aJsonNode["dimensions"][1].AsInt;
			List<int> list = new List<int>();
			foreach (object obj in aJsonNode["angles"].AsArray)
			{
				JSONNode jsonnode = (JSONNode)obj;
				list.Add(jsonnode.AsInt);
			}
			this.Enqueue(delegate
			{
				this._imageMaker.TakeScreenshot("Person", value, value2, list, asInt, asInt2);
			});
		}

		private void WorkerThread()
		{
			while (!this._exitThread)
			{
				this._event.WaitOne();
				object @lock = this._lock;
				lock (@lock)
				{
					while (this._queue.Count > 0)
					{
						this._queue.Dequeue()();
					}
				}
			}
		}

		public Foto2VamServer()
		{
			this._pipeServer = new PipeServer();
			this._pipeServer.RegisterHandler("screenshot", new Action<JSONNode>(this.HandleTakeScreenshot));
			this._imageMaker = new GameObject().AddComponent<ImageMaker>();
			this._thread = new Thread(new ThreadStart(this.WorkerThread));
			this._queue = new Queue<Action>();
			this._event = new AutoResetEvent(false);
			this._lock = new object();
			this._exitThread = false;
			this._thread.Start();
		}

		private void Enqueue(Action aAction)
		{
			object @lock = this._lock;
			lock (@lock)
			{
				this._queue.Enqueue(aAction);
			}
			this._event.Set();
		}

		private ImageMaker _imageMaker;

		private Thread _thread;

		private Queue<Action> _queue;

		private AutoResetEvent _event;

		private bool _exitThread;

		private object _lock;

		private PipeServer _pipeServer;
	}
}




namespace VamMod
{
	public class ImageMaker : MonoBehaviour
	{
		public ImageMaker()
		{
			this._event = new AutoResetEvent(false);
		}

		public void Update()
		{
			if (this.pendingAction != null)
			{
				try
				{
					this.pendingAction();
				}
				catch (Exception ex)
				{
					Debug.LogError("Exception: " + ex.ToString());
				}
				finally
				{
					this.pendingAction = null;
				}
			}
		}

		public void TakeScreenshot(string aName, string aJsonPath, string aOutputPath, List<int> aAngles, int aWidth, int aHeight)
		{
			this.pendingAction = delegate()
			{
				GameObject gameObject = GameObject.Find(aName);
				if (gameObject == null)
				{
					foreach (GameObject gameObject2 in UnityEngine.Object.FindObjectsOfType<GameObject>())
					{
						if (gameObject2.name.StartsWith(aName))
						{
							gameObject = gameObject2;
							break;
						}
					}
				}
				if (gameObject == null)
				{
					this._event.Set();
					return;
				}
				Atom component = gameObject.GetComponent<Atom>();
				component.LoadAppearancePreset(aJsonPath);
				this.StartCoroutine(this.TakeScreenshotCo(component, aOutputPath, aAngles, aWidth, aHeight));
			};
			this._event.WaitOne();
		}

		private IEnumerator TakeScreenshotCo(Atom atom, string aOutputPath, List<int> aAngles, int aWidth, int aHeight)
		{
			Camera camera = new GameObject().AddComponent<Camera>();
			camera.name = "CoCamera";
			camera.enabled = true;
			camera.fieldOfView = 20f;
			RenderTexture renderTexture = new RenderTexture(aWidth, aHeight, 24);
			camera.targetTexture = renderTexture;
			Texture2D texture2d = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGB24, false);
			Component head = null;
			Component component = null;
			foreach (Rigidbody rigidbody in atom.rigidbodies)
			{
				if (rigidbody.name == "head")
				{
					head = rigidbody;
				}
				else if (rigidbody.name == "headControl")
				{
					component = rigidbody;
				}
				if (null != component && null != head)
				{
					break;
				}
			}
			if (component.transform.rotation != Quaternion.identity)
			{
				component.transform.SetPositionAndRotation(component.transform.position, Quaternion.identity);
			}
			SuperController.singleton.HideMainHUD();
			Vector3 prevPos = head.transform.position;
			for (;;)
			{
				yield return null;
				if (!SuperController.singleton.IsSimulationPaused() && (double)(prevPos - head.transform.position).sqrMagnitude <= 0.1)
				{
					break;
				}
				prevPos = head.transform.position;
			}
			foreach (int angle in aAngles)
			{
				camera.transform.SetPositionAndRotation(head.transform.position + new Vector3(0f, 0f, 1f), Quaternion.identity);
				camera.transform.RotateAround(head.transform.position, Vector3.up, (float)angle);
				camera.transform.LookAt(head.transform);
				yield return null;
				RenderTexture.active = renderTexture;
				texture2d.ReadPixels(new Rect(0f, 0f, (float)renderTexture.width, (float)renderTexture.height), 0, 0);
				texture2d.Apply();
				byte[] bytes = texture2d.EncodeToPNG();
				File.WriteAllBytes(string.Concat(new string[]
				{
					aOutputPath,
					"_",
					angle.ToString(),
					".png"
				}), bytes);
			}
			List<int>.Enumerator enumerator = default(List<int>.Enumerator);
			UnityEngine.Object.Destroy(camera);
			UnityEngine.Object.Destroy(texture2d);
			renderTexture.Release();
			this._event.Set();
			yield break;
			yield break;
		}

		private Action pendingAction;

		private AutoResetEvent _event;
	}
}




namespace VamMod
{
	public class PipeServer
	{
		public PipeServer()
		{
			this._connectionCallback = new AsyncCallback(this.HandleConnection);
			this._readCallback = new AsyncCallback(this.HandleRead);
			this._readBuffer = new byte[4096];
			this._handlers = new Dictionary<string, Action<JSONNode>>();
			this.StartServer();
		}

		private void HandleConnection(IAsyncResult ar)
		{
			this._pipeServer.EndWaitForConnection(ar);
			if (this._pipeServer.IsConnected)
			{
				this._pipeServer.BeginRead(this._readBuffer, 0, this._readBuffer.Length, this._readCallback, null);
			}
		}

		private void HandleRead(IAsyncResult ar)
		{
			int num = this._pipeServer.EndRead(ar);
			if (num == 0)
			{
				this.Disconnect();
				return;
			}
			this._recvdString += Encoding.Default.GetString(this._readBuffer, 0, num);
			string text = "<EOM>";
			int num2;
			while ((num2 = this._recvdString.IndexOf(text)) >= 0)
			{
				string aJSON = this._recvdString.Substring(0, num2);
				this._recvdString = this._recvdString.Substring(num2 + text.Length);
				JSONNode aMsg = JSON.Parse(aJSON);
				this.HandleMessage(aMsg);
			}
			this._pipeServer.BeginRead(this._readBuffer, 0, this._readBuffer.Length, this._readCallback, null);
		}

		private void Disconnect()
		{
			if (this._pipeServer.IsConnected)
			{
				this._pipeServer.Disconnect();
			}
			this._pipeServer.Dispose();
			this._pipeServer = null;
			this.StartServer();
		}

		~PipeServer()
		{
			this.Disconnect();
		}

		private void StartServer()
		{
			if (this._pipeServer != null)
			{
				this.Disconnect();
			}
			this._pipeServer = new NamedPipeServerStream("foto2vamPipe", PipeDirection.InOut);
			this._pipeServer.BeginWaitForConnection(this._connectionCallback, null);
		}

		private void HandleMessage(JSONNode aMsg)
		{
			string value = aMsg["cmd"].Value;
			if (this._handlers.ContainsKey(value))
			{
				this._handlers[value](aMsg);
			}
		}

		public void RegisterHandler(string aCmd, Action<JSONNode> aHandler)
		{
			this._handlers[aCmd] = aHandler;
		}

		private AsyncCallback _connectionCallback;

		private AsyncCallback _readCallback;

		private NamedPipeServerStream _pipeServer;

		private byte[] _readBuffer;

		private string _recvdString;

		private Dictionary<string, Action<JSONNode>> _handlers;
	}
}
