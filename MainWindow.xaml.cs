﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using CSCore.Codecs;
using CSCore.CoreAudioAPI;
using CSCore.SoundOut;
using CSCore.Streams;
using Microsoft.WindowsAPICodePack.Dialogs;
namespace osu_song_player
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		private readonly MusicPlayer musicPlayer = new MusicPlayer();
		private readonly UserConfigManager userConfigManager = new UserConfigManager();
		private readonly SongFolderCrawler songFolderCrawler = new SongFolderCrawler();
		private readonly PlaylistSerializer serializer = new PlaylistSerializer();
		private readonly PlaylistCreator playlistCreator = new PlaylistCreator();
		private readonly ShuffleController shuffleController = new ShuffleController();

		private readonly ObservableCollection<MMDevice> devices = new ObservableCollection<MMDevice>();
		public ObservableCollection<PlaylistItemViewModel> PlaylistItems { get; } = new ObservableCollection<PlaylistItemViewModel>();

		private System.Windows.Threading.DispatcherTimer dispatcherTimer;

		public string selectedFolderPath;
		private SongViewModel currentSong;
		public PlaylistViewModel currentPlaylist = new PlaylistViewModel();
		public MainWindow()
		{
			InitializeComponent();
			LoadDevices(); //cscore, audio output devices

			PlaylistItems = new ObservableCollection<PlaylistItemViewModel>(serializer.GetAllPlaylists());
			playlistListBox.DataContext = this;
			playlistCountLabel.DataContext = this;
			targetPlaylistComboBox.DataContext = this;

			userConfigManager.DeserializeConfig();
			if (!userConfigManager.IsConfigEmpty)
			{
				//setting osu folder path
				selectedFolderPath = userConfigManager.Config.folderPath;
				selectedPathTextBlock.Text = userConfigManager.Config.folderPath;

				if(userConfigManager.Config.outputDeviceId != null)
				{
					for(int i = 0; i < devices.Count; i++)
					{
						//check for same devices
						if (userConfigManager.Config.outputDeviceId.Equals(devices[i].DeviceID))
						{
							audioOutputComboBox.SelectedIndex = i;
							Console.WriteLine("audio device loaded from config " + devices[i]);
							break;
						}
					}
				}

				musicPlayer.Volume = userConfigManager.Config.volume;
			}

			playlistInfoPanel.DataContext = currentPlaylist;
			songListBox.DataContext = currentPlaylist;
			songInfoGrid.DataContext = musicPlayer; //also sets the time slider and volume slider data context
			createProgessLabel.DataContext = playlistCreator;

			dispatcherTimer = new System.Windows.Threading.DispatcherTimer();
			dispatcherTimer.Tick += dispatcherTimerTick;
			dispatcherTimer.Interval = new TimeSpan(0, 0, 1);

			this.Closed += new EventHandler(ClosingCleanUp);

		}

		private void ClosingCleanUp(object sender, EventArgs e)
		{
			musicPlayer.End();
			ReserializeCurrentPlaylist();   //checks for changes, and reserialize it
			SerializeConfig();
			//Environment.Exit(Environment.ExitCode);
		}

		List<SongViewModel> temp = new List<SongViewModel>();
		private void SelectPathBtn_Click(object sender, RoutedEventArgs e)
		{

			CommonOpenFileDialog dialog = new CommonOpenFileDialog();
			dialog.InitialDirectory = "C:\\Users";
			dialog.IsFolderPicker = true;
			if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
			{
				if (!dialog.FileName.Equals(selectedFolderPath))
				{
					LoadSongsFromPath(dialog.FileName);

					//if path changed, save it to config
					SerializeConfig();
				}
			}
		}

		private void LoadSongsFromPath(string path)
		{
			selectedFolderPath = path;
			selectedPathTextBlock.Text = path;

			//songFolderCrawler.SearchThreaded(path);
		}

		private void SerializeConfig()
		{
			userConfigManager.SetConfigValues(selectedFolderPath, ((MMDevice)audioOutputComboBox.SelectedItem).DeviceID, musicPlayer.Volume);
			userConfigManager.SerializeConfig();
		}

		private void LoadDevices()
		{
			using (var mmdeviceEnumerator = new MMDeviceEnumerator())
			{
				using (
					var mmdeviceCollection = mmdeviceEnumerator.EnumAudioEndpoints(DataFlow.Render, DeviceState.Active))
				{
					foreach (var device in mmdeviceCollection)
					{
						devices.Add(device);
					}
				}
			}
			audioOutputComboBox.ItemsSource = devices;
			audioOutputComboBox.SelectedIndex = 0;
		}

		private void CtrlPlayBtn_Click(object sender, RoutedEventArgs e)
		{
			PlaySong();
			musicPlayer.Update();
			dispatcherTimer.Start();
		}
		private void PlaySong()
		{
			//get selected song source from the currently selected tab. "all song" and "search" tab
			SongViewModel selected = songListTabControl.SelectedIndex == 1 ?
					(SongViewModel)searchListbox.SelectedItem : (SongViewModel)songListBox.SelectedItem;
			if (selected == null)
				return;
			if (!musicPlayer.HasAudio || !currentSong.CheckEquals(selected))
			{
				currentSong = selected;
				Console.WriteLine("playing: " + selectedFolderPath + "\\" + currentSong.Path);
				musicPlayer.Open(currentSong, selectedFolderPath + "\\" + currentSong.Path, (MMDevice)audioOutputComboBox.SelectedItem);
				Console.WriteLine((MMDevice)audioOutputComboBox.SelectedItem);
			}

			musicPlayer.Play();
		}

		private void CtrlPauseBtn_Click(object sender, RoutedEventArgs e)
		{
			musicPlayer.Pause();
			dispatcherTimer.Stop();
		}

		//not used, but might need it later. It works, just need to add this to a button
		//private void CtrlStopBtn_Click(object sender, RoutedEventArgs e)
		//{
		//	musicPlayer.Stop();
		//	dispatcherTimer.Stop();
		//}

		private void dispatcherTimerTick(object sender, EventArgs e)
		{
			if (!musicPlayer.HasAudio)
				return;

			musicPlayer.Update();
			if (!musicPlayer.IsPlaying && !musicPlayer.manualStop)
			{
				int nextIndex, maximum;
				ListBox listbox;
				if(songListTabControl.SelectedIndex == 1)		//if in search box
				{
					nextIndex = searchListbox.SelectedIndex + 1;
					maximum = searchListbox.Items.Count;
					listbox = searchListbox;
				}
				else	//main window
				{
					nextIndex = songListBox.SelectedIndex + 1;
					maximum = currentPlaylist.SongCount;
					listbox = songListBox;
				}


				musicPlayer.ResetCurrentTime();
				musicPlayer.Update();
				if ((bool)ctrlRepeatCheckBox.IsChecked)
				{
					if (listbox.SelectedItem != null)
						PlaySong();
					else
						dispatcherTimer.Stop();
					return;
				}

				if ((bool)ctrlShuffleCheckBox.IsChecked && maximum > 1)
				{
					//random value that doesnt repeat
					listbox.SelectedIndex = shuffleController.GetNextValue(maximum, listbox.SelectedIndex);
					PlaySong();
					listbox.ScrollIntoView(listbox.SelectedItem);
				}
				else
				{
					//so it doesnt exceed the list 
					if (nextIndex < maximum)
					{
						listbox.SelectedIndex++;
						PlaySong();
					}
					else    //reset song to beginning 
					{
						dispatcherTimer.Stop();
					}
				}
			}
		}

		private void AudioOutputComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (audioOutputComboBox.SelectedItem != null)
			{
				musicPlayer.Stop();
				musicPlayer.SetDevice((MMDevice)audioOutputComboBox.SelectedItem);
				userConfigManager.SetConfigValues(((MMDevice)audioOutputComboBox.SelectedItem).DeviceID);
			}
		}

		private bool tempIsPreviouslyPlaying = false;

		private void TimeSlider_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
		{
			tempIsPreviouslyPlaying = musicPlayer.IsPlaying;
			musicPlayer.Pause();
			Console.WriteLine("started drag");
		}

		private void TimeSlider_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
		{
			musicPlayer.Update();
			Console.WriteLine("dragging");
		}

		private void TimeSlider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
		{
			if (tempIsPreviouslyPlaying)
				musicPlayer.Play();
			Console.WriteLine("finish drag");
		}
		private void TimeSlider_MouseUp(object sender, MouseButtonEventArgs e)
		{
			musicPlayer.Update();
		}


		private void PlaylistListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			ReserializeCurrentPlaylist();   //checks for changes, and reserialize it
			if((bool)ctrlShuffleCheckBox.IsChecked)
				shuffleController.ClearPastValues();
			int index = playlistListBox.SelectedIndex;
			if (index >= 0 && index < PlaylistItems.Count)
			{
				PlaylistItemViewModel item = PlaylistItems[index];
				PlaylistViewModel playlist = serializer.DeserializePlaylist(item.Path);
				if (playlist != null)
					currentPlaylist.UpdateProperties(playlist);
				if (currentPlaylist.Songs.Count > 0)
				{
					songListBox.SelectedIndex = 0;
				}
			}
		}

		private void VolumeSlider_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
		{
			//volumeValueLabel.Content = musicPlayer.Volume.ToString("F0");
		}

		private void SearchButton_Click(object sender, RoutedEventArgs e)
		{
			SongSearcher searcher = new SongSearcher();
			searchListbox.ItemsSource = searcher.Search(searchTextBox.Text, new List<SongViewModel>(currentPlaylist.Songs), (bool)ignoreTxSizeCheckBox.IsChecked);
			searchFoundCountLabel.Content = searchListbox.Items.Count;
		}

		private void CreatePlaylistButton_Click(object sender, RoutedEventArgs e)
		{
			if(newPlaylistTextbox.Text.Length == 0)
			{
				MessageBox.Show("Playlist must have a name", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
				return;
			}

			if (playlistCreator.inProgress && (bool)createConditionCheckBox.IsChecked )
			{
				MessageBox.Show("Cannot create playlist due to ongoing operation", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
				return;
			}

			for(int i = 0; i < PlaylistItems.Count; i++)
			{
				if (PlaylistItems[i].Name.Equals(newPlaylistTextbox.Text, StringComparison.InvariantCultureIgnoreCase))
				{
					MessageBox.Show("Name already exist", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
					return;
				}
			}


			if ((bool)createConditionCheckBox.IsChecked )
			{
				playlistCreator.events += AddToPlaylistItemListFromCreator;
				playlistCreator.CreatePlaylist(newPlaylistTextbox.Text, selectedFolderPath);
				Console.WriteLine("called");
				MessageBox.Show("All songs from the osu! folder are being fetched, and the playlist will be added when operation is complete. This might take a while."
					, "Information", MessageBoxButton.OK, MessageBoxImage.Information);
			}
			else
			{
				PlaylistViewModel playlist = playlistCreator.CreatePlaylist(newPlaylistTextbox.Text);
				PlaylistItems.Add(new PlaylistItemViewModel(playlist.Name, serializer.GetPlaylistPath(playlist)));
			}
			newPlaylistTextbox.Text = "";
			createConditionCheckBox.IsChecked = false;
		}

		public void AddToPlaylistItemListFromCreator(object sender, EventArgs e)
		{
			if (playlistCreator.tempPlaylist == null)
				return;
			this.Dispatcher.Invoke(() =>
			{
				PlaylistItems.Add(new PlaylistItemViewModel(playlistCreator.tempPlaylist.Name, serializer.GetPlaylistPath(playlistCreator.tempPlaylist)));
			});

			Console.WriteLine("added to list");
			MessageBox.Show(playlistCreator.tempPlaylist.Name + " has been added to playlist", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
			playlistCreator.tempPlaylist = null;
		}

		//moving songs from playlist to playlist
		private void AddToPlaylistButton_Click(object sender, RoutedEventArgs e)
		{
			List<SongViewModel> selectedSongs;
			switch (songListTabControl.SelectedIndex)
			{
				case 1:     //tab 1, search tab
					selectedSongs = searchListbox.SelectedItems.Cast<SongViewModel>().ToList();
					break;
				default:    //tab 0, song list
					selectedSongs = songListBox.SelectedItems.Cast<SongViewModel>().ToList();
					break;
			}

			if (selectedSongs.Count <= 0)
			{
				MessageBox.Show("No songs are selected", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
				return;
			}

			if (((PlaylistItemViewModel)targetPlaylistComboBox.SelectedItem).Name.Equals(currentPlaylist.Name))
			{
				MessageBox.Show("The source and destination playlist cannot be the same", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
				return;
			}



			PlaylistItemViewModel item = PlaylistItems[targetPlaylistComboBox.SelectedIndex];
			PlaylistViewModel targetPlaylist = serializer.DeserializePlaylist(item.Path);
			MessageBoxResult result = MessageBox.Show(string.Format("Would you like to copy {0} songs from {1} to {2}", selectedSongs.Count, currentPlaylist.Name, targetPlaylist.Name), "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);

			if (result == MessageBoxResult.Yes)
			{
				int largestInd = 0;
				for (int i = 0; i < targetPlaylist.Songs.Count; i++)
				{
					if (targetPlaylist.Songs[i].Order > largestInd)
						largestInd = targetPlaylist.Songs[i].Order;
				}
				for (int i = 0; i < selectedSongs.Count; i++)
				{
					SongViewModel song = new SongViewModel(selectedSongs[i]);
					song.Order = largestInd + i + 1;
					targetPlaylist.Songs.Add(song);
				}

				serializer.Serialize(targetPlaylist);
				Console.WriteLine("moved " + selectedSongs.Count + " to " + targetPlaylist.Name);
			}
		}

		private void DeleteSongButton_Click(object sender, RoutedEventArgs e)
		{
			if (songListBox.SelectedItems.Count <= 0)
			{
				Console.WriteLine("cannot delete song", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
				return;
			}
			if(songListTabControl.SelectedIndex == 1)
			{
				MessageBox.Show("Please return to the \"All Songs\" to delete songs", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
				return;
			}

			currentPlaylist.changed = true;
			List<SongViewModel> selectedSongs = songListBox.SelectedItems.Cast<SongViewModel>().ToList();

			MessageBoxResult result = MessageBox.Show(string.Format("Would you like to delete {0} songs from {1}", selectedSongs.Count, currentPlaylist.Name), "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);

			if (result == MessageBoxResult.Yes)
			{
				for (int i = 0; i < selectedSongs.Count; i++)
				{
					Console.WriteLine("Removed " + selectedSongs[i].ToString());
					currentPlaylist.Songs.Remove(selectedSongs[i]);
				}

				songListBox.SelectedIndex = 0;
				for (int i = 0; i < currentPlaylist.Songs.Count; i++)
				{
					if (currentPlaylist.Songs[i].CheckEquals(currentSong))
						songListBox.SelectedIndex = i;
				}
			}

			
		}

		private void ReserializeCurrentPlaylist()
		{
			if (currentPlaylist.changed)
			{
				Console.WriteLine("reserialized changed playlist: " + currentPlaylist.Name);
				serializer.Serialize(currentPlaylist);
				currentPlaylist.changed = false;
			}
		}

		private void DeletePlaylistButton_Click(object sender, RoutedEventArgs e)
		{
			if (PlaylistItems.Count <= 0)
			{
				Console.WriteLine("Cannot delete");
				return;
			}

			MessageBoxResult result = MessageBox.Show(string.Format("Would you like to delete {0}", currentPlaylist.Name), "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);

			if (result == MessageBoxResult.Yes)
			{

				int index = playlistListBox.SelectedIndex;
				string path = PlaylistItems[index].Path;
				serializer.DeletePlaylistFile(path);
				PlaylistItems.RemoveAt(index);
				if (PlaylistItems.Count > 0)
				{
					playlistListBox.SelectedIndex = 0;
					if (currentPlaylist.Songs.Count > 0)
					{
						songListBox.SelectedIndex = 0;
					}
				}
				else
				{
					songListBox.Items.Refresh();
				}
			}
		}

		private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if(mainTabControl.SelectedIndex == 2)
			{
				if(playlistListBox.SelectedIndex >= 0 && currentPlaylist != null)
				{
					renameTextBox.Text = currentPlaylist.Name;
				}
			}
		}

		private void ConfirmRenameButton_Click(object sender, RoutedEventArgs e)
		{
			if(PlaylistItems.Count <= 0)
			{
				MessageBox.Show("No playlist to rename", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
				return;
			}
			if(renameTextBox.Text.Length <= 0)
			{
				MessageBox.Show("Name cannot be empty", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
				return;
			}
			if (renameTextBox.Text.Equals(currentPlaylist.Name))
			{
				MessageBox.Show("Name is the same", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
				return;
			}

			string before = currentPlaylist.Name;
			currentPlaylist.Name = renameTextBox.Text;
			PlaylistItems[playlistListBox.SelectedIndex].Name = renameTextBox.Text;
			string newPath = serializer.RenamePlaylist(PlaylistItems[playlistListBox.SelectedIndex].Path, renameTextBox.Text);
			PlaylistItems[playlistListBox.SelectedIndex].Path = newPath;


			MessageBox.Show("Playlist renamed from " + before + " to " + currentPlaylist.Name, "Information", MessageBoxButton.OK, MessageBoxImage.Information);
		}

		private void CtrlPreviousBtn_Click(object sender, RoutedEventArgs e)
		{
			SkipSong(false);
		}

		private void CtrlNextBtn_Click(object sender, RoutedEventArgs e)
		{
			SkipSong(true);
		}

		private void SkipSong(bool forward)
		{
			if (!musicPlayer.IsPlaying)
				return;
			ListBox listbox;
			int maximum;
			if(songListTabControl.SelectedIndex == 1)
			{
				listbox = searchListbox;
				maximum = searchListbox.Items.Count;
			}
			else
			{
				listbox = songListBox;
				maximum = currentPlaylist.Songs.Count;
			}

			if ((bool)ctrlShuffleCheckBox.IsChecked && maximum > 1)
			{
				listbox.SelectedIndex = shuffleController.GetNextValue(maximum, listbox.SelectedIndex);
				PlaySong();
				listbox.ScrollIntoView(listbox.SelectedItem);
				return;
			}

			if (forward)
			{
				if (listbox.SelectedIndex + 1 < maximum)
				{
					listbox.SelectedIndex++;
					PlaySong();
				}
			}
			else
			{
				if (listbox.SelectedIndex - 1 >= 0)
				{
					listbox.SelectedIndex--;
					PlaySong();
				}
			}
		}

		private void SongListTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (e.Source is TabControl)
			{
				Console.WriteLine("cleared at tab control");
				shuffleController.ClearPastValues();
			}
		}

		private void PlaySong_MouseDoubleClick(object sender, MouseButtonEventArgs e)
		{
			PlaySong();
			musicPlayer.Update();
			dispatcherTimer.Start();
		}
	}
}
