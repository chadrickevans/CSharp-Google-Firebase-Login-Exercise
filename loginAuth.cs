using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Firebase;
using Firebase.Auth;
using Firebase.Firestore;
using Firebase.Storage;
using UnityEngine.UI;
using Firebase.Extensions;
using System;
using UnityEngine.SceneManagement;
using TMPro;

public class loginAuth : MonoBehaviour
{

	[Header("Firebase")]
	public firebaseManager FBM;

	[Header("Inputs")]
	public InputField login_email;
	public InputField login_pw;

	public InputField register_email;
	public InputField register_pw;
	public TMP_InputField register_un;

	float cooldown;

	[System.Serializable]
	public class errorMsgs {
		public Text login_error;
		public Text reg_error;
		public Text reg_username_error;
	}

	public errorMsgs eMSG;

	private void Start() {
		FBM = (firebaseManager)FindObjectOfType(typeof(firebaseManager));
		resetErrorMSG();
	}

	void resetErrorMSG() {
		eMSG.login_error.text = "";
		eMSG.reg_error.text = "";
		eMSG.reg_username_error.text = "";
	}

	public static int CalculateAge(DateTime BirthDate) {
		int yearsPassed = DateTime.Now.Year - BirthDate.Year;
		
		if ((DateTime.Now.Month == BirthDate.Month && DateTime.Now.Day < BirthDate.Day) || DateTime.Now.Month < BirthDate.Month) {
			yearsPassed--;
		}
		return yearsPassed;
	}

	public void registerAcctButton() {
		if (cooldown > 0) { return; }
		StartCoroutine(Register(register_email.text, register_pw.text, register_un.text)); cooldown = 5;
	}

	public void loginAcctButton() {
		if (cooldown > 0) { return; }
		StartCoroutine(Login(login_email.text, login_pw.text)); cooldown = 5;
	}

	public InputField monthChooser;
	public InputField dayChooser;
	public InputField yearChooser;

	public Text status;

	private IEnumerator Login(string _email , string _password) {
		var LoginTask = FBM.auth.SignInWithEmailAndPasswordAsync(_email , _password);
		yield return new WaitUntil(predicate: () => LoginTask.IsCompleted);

		if (LoginTask.Exception != null) {
			//If there are errors handle them
			FirebaseException firebaseEx = LoginTask.Exception.GetBaseException() as FirebaseException;
			AuthError errorCode = (AuthError)firebaseEx.ErrorCode;

			loadBlocker.SetActive(false);
			string message = "Login Failed!";
			switch (errorCode) {
				case AuthError.MissingEmail:
					message = "Missing Email";
					eMSG.login_error.text = "ADD AN EMAIL";
					break;
				case AuthError.MissingPassword:
					message = "Missing Password";
					eMSG.login_error.text = "ADD A PASSWORD";
					break;
				case AuthError.WrongPassword:
					message = "Wrong Password";
					eMSG.login_error.text = "INVALID PASSWORD";
					break;
				case AuthError.InvalidEmail:
					message = "Invalid Email";
					eMSG.login_error.text = "INVALID EMAIL";
					break;
				case AuthError.UserNotFound:
					message = "Account does not exist";
					eMSG.login_error.text = "ACCOUNT DOES NOT EXIST";
					break;
			}
		}
		else {
			//User is now logged in
			//Now get the result

			FBM.User = LoginTask.Result;
			FBM.loggedIn = true;
			Debug.LogFormat("User signed in successfully: {0} ({1}) {2}", FBM.User.DisplayName , FBM.User.Email, FBM.User.UserId);
			

			FBM.firestore.Collection("users").Document(FBM.User.UserId).GetSnapshotAsync().ContinueWithOnMainThread(task =>
			{
				DocumentSnapshot snapshot = task.Result;
				if (snapshot.Exists) {
					firebaseManager.USERfb tempData = snapshot.ConvertTo<firebaseManager.USERfb>();
					FBM.FBUserToLocal(tempData , FBM.yourInfo);
					StartCoroutine(nowLog(_email, _password));
				}
				else {
					loadBlocker.SetActive(false);
					Debug.Log(String.Format("Document {0} does not exist!" , snapshot.Id));
				}
			});			
			
		}
	}

	private IEnumerator Register(string _email, string _password, string _username) {
		if (_username == "") {
			//If the username field is blank show a warning
			eMSG.reg_username_error.text = "MUST ADD USERNAME";
		}
		else {
			var RegisterTask = FBM.auth.CreateUserWithEmailAndPasswordAsync(_email, _password);
			yield return new WaitUntil(predicate: () => RegisterTask.IsCompleted);

			if (RegisterTask.Exception != null) {
				switch (errorCode) {
					case AuthError.MissingEmail:
						eMSG.reg_error.text = "MUST ADD EMAIL";
						break;
					case AuthError.MissingPassword:
						eMSG.reg_error.text = "MUST ADD PASSWORD";
						break;
					case AuthError.WeakPassword:
						eMSG.reg_error.text = "PASSWORD IS TOO WEAK";
						break;
					case AuthError.EmailAlreadyInUse:
						eMSG.reg_error.text = "EMAIL IS ALREADY USED";
						break;
				}
			}
			else {
				//User has now been created
				//Now get the result

				yield return new WaitUntil(() => RegisterTask.Result != null);

				//check if you have an existing account. Remove the registered account if so.
				CollectionReference accRef = FBM.firestore.Collection("users");
				Query query = accRef.WhereEqualTo("dID", SystemInfo.deviceUniqueIdentifier);

				string result = null;
				bool accountExistsOnDevice = false;

				query.GetSnapshotAsync().ContinueWithOnMainThread((querySnapshotTask) => {
					foreach (DocumentSnapshot documentSnapshot in querySnapshotTask.Result.Documents) { accountExistsOnDevice = true; }
					if (querySnapshotTask.IsCompleted) { result = "done."; }
				});

				yield return new WaitUntil(() => result != null);

				if (accountExistsOnDevice) {
					if (FBM.auth.CurrentUser != null && FBM.auth.CurrentUser.UserId == RegisterTask.Result.UserId) {
						FBM.auth.CurrentUser.DeleteAsync().ContinueWith(task => { 
							Debug.Log("Duplicate device account deleted successfully."); 
						});
					}

					screen = "login";
					yield break;
				}

				FBM.User = RegisterTask.Result;

				if (FBM.User != null) {
					UserProfile profile = new UserProfile { DisplayName = _username };
					var ProfileTask = FBM.User.UpdateUserProfileAsync(profile);

					yield return new WaitUntil(() => ProfileTask.IsCompleted);

					if (ProfileTask.Exception != null) {
						eMSG.reg_username_error.text = "FAILED TO SET USERNAME.";
					}
					else {
						//Username is now set
						//Now return to login screen
						firebaseManager.USERfb documentData = new firebaseManager.USERfb();

						DateTime nD = new DateTime(int.Parse(yearChooser.text), int.Parse(monthChooser.text), int.Parse(dayChooser.text));
						documentData.age = CalculateAge(nD);

						documentData.dID = SystemInfo.deviceUniqueIdentifier;
						documentData.displayname = FBM.User.DisplayName;

						FBM.firestore.Collection("users").Document(FBM.User.UserId).SetAsync(documentData).ContinueWith(task => {
							if (task.IsCompleted) {
								Debug.Log("created a new account.");
							}
						});

						resetErrorMSG();
					}
				}
			}
		}
	}

}
