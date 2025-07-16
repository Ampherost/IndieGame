using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;


public class SceneManagerScript : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        Debug.Log("Starting");
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void LoadMainMenu()
    {
        SceneManager.LoadScene("MainMenuScene");  
    }

    public void LoadExploration()
    {
        SceneManager.LoadScene("ExplorationScene"); 
    }

    public void LoadOverworld()
    {
        SceneManager.LoadScene("OverworldScene"); 
    }

    public void LoadCombat()
    {
        SceneManager.LoadScene("CombatScene"); 
    }

    public void EndGame()
    {
        Application.Quit(); // Quits game

        Debug.Log("Game has been quit."); // Debug message to confirm the function is called (only visible in the Unity editor)
    }

}
