@echo off
color 0A
echo ===================================================
echo     INPUTBRIDGE DEBUG VERSIYONU OLUSTURUCU
echo ===================================================
echo.
echo Lutfen bekleyin, Debug (Hata Ayiklama) surumu hazirlaniyor...
echo.

if exist "publish\InputBridge_DEBUG" rmdir /s /q "publish\InputBridge_DEBUG"
if exist "publish\InputBridge_DEBUG_Test.zip" del /f /q "publish\InputBridge_DEBUG_Test.zip"

mkdir "publish\InputBridge_DEBUG"
mkdir "publish\InputBridge_DEBUG\Host_App"
mkdir "publish\InputBridge_DEBUG\Client_App"

echo [1/4] Host (Ana Bilgisayar) uygulamasi derleniyor...
dotnet publish src/InputBridge.Host/InputBridge.Host.csproj -c Debug -r win-x64 --self-contained -p:PublishSingleFile=false -o publish/InputBridge_DEBUG/Host_App/ >nul 2>&1

echo [2/4] Client (Ikinci Bilgisayar) uygulamasi derleniyor...
dotnet publish src/InputBridge.Client/InputBridge.Client.csproj -c Debug -r win-x64 --self-contained -p:PublishSingleFile=false -o publish/InputBridge_DEBUG/Client_App/ >nul 2>&1

echo [3/4] Talimat dosyasi ve baslaticilar olusturuluyor...
echo DEBUG TEST TALIMATLARI: > publish/InputBridge_DEBUG/NASIL_TEST_EDILIR.txt
echo ======================= >> publish/InputBridge_DEBUG/NASIL_TEST_EDILIR.txt
echo 1. Bu klasoru hem kendi bilgisayariniza hem de baglanacaginiz 2. bilgisayara atin. >> publish/InputBridge_DEBUG/NASIL_TEST_EDILIR.txt
echo 2. Ana bilgisayarda '1_ANA_BILGISAYAR_BASLAT.bat' dosyasina cift tiklayin. >> publish/InputBridge_DEBUG/NASIL_TEST_EDILIR.txt
echo 3. Diger bilgisayarda '2_IKINCI_BILGISAYAR_BASLAT.bat' dosyasina cift tiklayin. >> publish/InputBridge_DEBUG/NASIL_TEST_EDILIR.txt
echo 4. Siyah ekran olayini tamamen kaldirdim, sadece arka planda log tutacaklar. >> publish/InputBridge_DEBUG/NASIL_TEST_EDILIR.txt
echo 5. Iki bilgisayari birbirine baglayin ve baglantinin daha onceki gibi "kendi kendine kopmasini" bekleyin. >> publish/InputBridge_DEBUG/NASIL_TEST_EDILIR.txt
echo 6. Baglanti koptugunda hemen kopuk mu kalacak yoksa aninda tekrar mi baglanacak onu gozlemleyin! >> publish/InputBridge_DEBUG/NASIL_TEST_EDILIR.txt
echo 7. Sorun yasarsaniz Windows uzerinden baslat menusu yandaki arama kismina %%APPDATA%%\InputBridge\logs >> publish/InputBridge_DEBUG/NASIL_TEST_EDILIR.txt
echo    yazip .log uzantili metin belgelerini bana gonderin. >> publish/InputBridge_DEBUG/NASIL_TEST_EDILIR.txt

echo @echo off > publish/InputBridge_DEBUG/1_ANA_BILGISAYAR_BASLAT.bat
echo cd Host_App >> publish/InputBridge_DEBUG/1_ANA_BILGISAYAR_BASLAT.bat
echo start InputBridge.Host.exe >> publish/InputBridge_DEBUG/1_ANA_BILGISAYAR_BASLAT.bat

echo @echo off > publish/InputBridge_DEBUG/2_IKINCI_BILGISAYAR_BASLAT.bat
echo cd Client_App >> publish/InputBridge_DEBUG/2_IKINCI_BILGISAYAR_BASLAT.bat
echo start InputBridge.Client.exe >> publish/InputBridge_DEBUG/2_IKINCI_BILGISAYAR_BASLAT.bat

echo [4/4] ZIP dosyasi haline getiriliyor...
powershell -Command "Compress-Archive -Path publish/InputBridge_DEBUG/* -DestinationPath publish/InputBridge_DEBUG_Test.zip -Force"

echo.
echo ===================================================
echo ISLEM BASARIYLA TAMAMLANDI! 
echo ===================================================
echo.
echo TEST ZIP DOSYASI BURADA: 
echo -^> publish\InputBridge_DEBUG_Test.zip
echo.
echo Dosyayi kolayca bulabilmen icin klasoru aciyorum...
explorer "publish"
pause
