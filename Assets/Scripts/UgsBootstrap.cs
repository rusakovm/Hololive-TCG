using System;
using System.Threading.Tasks;

using UnityEngine;

using Unity.Services.Core;
using Unity.Services.Authentication;

namespace HololiveTCG.Unity
{
    /// <summary>
    /// Вход в Unity Gaming Services.
    ///
    /// Relay и Lobby не работают без этого:
    /// сначала надо поднять службы и получить
    /// игроку удостоверение. Вход анонимный —
    /// ни почты, ни пароля, идентификатор
    /// сохраняется на устройстве.
    ///
    /// Отдельный компонент, потому что вход нужен
    /// один раз на запуск, а не на каждую партию.
    /// </summary>
    public class UgsBootstrap : MonoBehaviour
    {
        /// <summary>
        /// Уже вошли и можно создавать комнаты.
        /// </summary>
        public static bool Ready { get; private set; }


        /// <summary>
        /// Что происходит сейчас — для показа
        /// в меню.
        /// </summary>
        public static string Status { get; private set; }


        /// <summary>
        /// Идентификатор игрока. Пригодится,
        /// чтобы отличать своих в лобби.
        /// </summary>
        public static string PlayerId { get; private set; }


        private static bool _started;


        private async void Awake()
        {
            await SignIn();
        }


        /// <summary>
        /// Поднять службы и войти.
        ///
        /// Можно звать сколько угодно раз:
        /// повторные вызовы ничего не делают.
        /// </summary>
        public static async Task SignIn()
        {
            if (Ready || _started)
                return;


            _started = true;

            try
            {
                Status = "подключаемся к службам Unity";

                await UnityServices.InitializeAsync();


                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    Status = "входим";

                    await AuthenticationService
                        .Instance
                        .SignInAnonymouslyAsync();
                }


                PlayerId =
                    AuthenticationService.Instance.PlayerId;

                Ready = true;

                Status = "готово";

                Debug.Log(
                    "Unity Services: вошли как " + PlayerId
                );
            }
            catch (Exception error)
            {
                _started = false;

                Status =
                    "не удалось войти: " + error.Message;

                Debug.LogError(
                    "Unity Services: " + error
                );
            }
        }
    }
}