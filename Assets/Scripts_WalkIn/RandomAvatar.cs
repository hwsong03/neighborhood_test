using UnityEngine;
using Meta.XR.MultiplayerBlocks.NGO;
using Oculus.Avatar2;
using Meta.XR.MultiplayerBlocks.Shared;

public class RandomAvatar : MonoBehaviour
{

    public void Start()
    {
        




    }



    public void Update()
    {

        GameObject go = null;

        try
        {
            go = GameObject.Find("LocalAvatar");




            //if (go.GetComponent<AvatarBehaviourNGO>().LocalAvatarIndex != AvatarIndexForUser[type])
            //    go.GetComponent<AvatarBehaviourNGO>().LocalAvatarIndex = AvatarIndexForUser[type];

            // 그냥 말 그대로 index만 바꾼거같은데
            //Debug.Log(go.GetComponent<AvatarBehaviourNGO>().);




        }
        catch { }



    }


}
