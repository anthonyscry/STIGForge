This directory is used with any content that includes OVAL wuaupdatesearcher / Microsoft's WUA (Windows Update Agent) API tests.  

For the wuaupdatesearcher tests, SCC performs the following:
1.  Looks in this directory for a file called wsusscn2.cab, if found attempt to use it.
    - If this file is older than 30 days, report an error that data may be inaccurate. 
	
2.  If an offline wsusscn2.cab is not found, check to see if the scanning target is configured to obtain updates from a local WSUS server. 
    - If configured to use WSUS, use WSUS server for currently available and approved patches.
	
3.  If neither the offline wsusscn2.cab or WSUS is found, then connect to Microsoft and query Windows Update directly.


To obtain a current wsusscn2.cab from Microsoft:  https://catalog.s.download.windowsupdate.com/microsoftupdate/v6/wsusscan/wsusscn2.cab