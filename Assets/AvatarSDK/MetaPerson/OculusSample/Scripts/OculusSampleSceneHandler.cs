/* Copyright (C) Itseez3D, Inc. - All Rights Reserved
* You may not use this file except in compliance with an authorized license
* Unauthorized copying of this file, via any medium is strictly prohibited
* Proprietary and confidential
* UNLESS REQUIRED BY APPLICABLE LAW OR AGREED BY ITSEEZ3D, INC. IN WRITING, SOFTWARE DISTRIBUTED UNDER THE LICENSE IS DISTRIBUTED ON AN "AS IS" BASIS, WITHOUT WARRANTIES OR
* CONDITIONS OF ANY KIND, EITHER EXPRESS OR IMPLIED
* See the License for the specific language governing permissions and limitations under the License.
* Written by Itseez3D, Inc. <support@avatarsdk.com>, July 2025
*/

using AvatarSDK.MetaPerson.Loader;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

namespace AvatarSDK.MetaPerson.OculusLipSync
{
	public class OculusSampleSceneHandler : MonoBehaviour
	{
		public Button button;
		public MetaPersonLoader loader;
		public GameObject dstObject;
		public Text progressText;
		public AudioSource audioSource;
		public GameObject existingAvatar;

		public OVRLipSyncContextMorphTarget headContextMorphTarget;
		public OVRLipSyncContextMorphTarget teethContextMorphTarget;
		public OVRLipSyncContext lipSyncContext;

		const string avatarUri = "https://metaperson.avatarsdk.com/avatars/b255d298-7644-48ec-85ef-4a2200668458/model.glb";
		// Start is called before the first frame update
		void Start()
		{
			if (progressText != null)
			{
				progressText.gameObject.SetActive(false);
			}
			else
			{
				Debug.LogWarning("[OculusSampleSceneHandler] progressText is not assigned in Inspector.");
			}

			if (button != null)
			{
				button.onClick.AddListener(OnButtonClick);
			}
			else
			{
				Debug.LogWarning("[OculusSampleSceneHandler] button is not assigned in Inspector.");
			}
		}
		void ProgressReport(float progress)
		{
			if (progressText != null)
			{
				progressText.text = string.Format("Downloading avatar: {0}%", (int)(progress * 100));
			}
		}
		async void OnButtonClick()
		{
			if (button != null)
			{
				button.gameObject.SetActive(false);
			}
			if (progressText != null)
			{
				progressText.gameObject.SetActive(true);
			}

			await loader.LoadModelAsync(avatarUri, ProgressReport);
			if (progressText != null)
			{
				progressText.gameObject.SetActive(false);
			}

			AvatarSdkOculusTools.Configure(loader.avatarObject, dstObject);

			MetaPersonUtils.ReplaceAvatar(loader.avatarObject, existingAvatar);
		}
		// Update is called once per frame
		void Update()
		{

		}
	}
}
