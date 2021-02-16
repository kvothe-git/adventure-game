using System;
using UnityEngine;

public abstract class Saver : MonoBehaviour
{
    public Guid Id;
    public SaveData saveData;

    protected string key;

    private SceneController sceneController;

    private void Awake()
    {
        sceneController = FindObjectOfType<SceneController>();

        if (!sceneController)
            throw new UnityException("No s'ha trobat cap SceneController, assegura't que existeix un a l'escena Persistent");

        key = SetKey();
    }

    private void OnEnable()
    {
        sceneController.BeforeSceneUnload += Save;
        sceneController.AfterSceneLoad += Load;
    }

    private void OnDisable()
    {
        sceneController.BeforeSceneUnload -= Save;
        sceneController.AfterSceneLoad -= Load;
    }

    protected abstract string SetKey();
    protected abstract void Save();
    protected abstract void Load();
}
