import Foundation
import AppKit

extension AppModel {
    func joinCode(_ code:String){
        guard !paired else{return};cancelQr();pairingInProgress=true;pairingStatus=L.text("Windows wird im lokalen Netzwerk gesucht …");let device=history.preferences.device
        pairingTask=Task{do{let link=try await Task.detached{try CodePairing.resolve(code,device:device)}.value;guard !Task.isCancelled else{return};self.joinQr(link,viaCode:true)}catch{guard !Task.isCancelled else{return};self.pairingInProgress=false;self.pairingStatus=L.text("Code nicht gefunden oder abgelaufen. Öffne unter Windows einen neuen Code und prüfe, ob beide Geräte im selben Netzwerk sind.")}}
    }
    func scanQrImage(){do{if let code=try qrFromImage(){joinQr(code)}}catch{pairingStatus=L.text("Kein gültiger Kopra-Code im Bild gefunden.")}}
    func joinQr(_ code:String,viaCode:Bool=false){
        guard !paired else{pairingStatus=L.text("Bitte zuerst die bestehende Verbindung aufheben.");return}
        cancelQr();pairingViaCode=viaCode;showQrCamera=false;settings=true;settingsTab=2;pairingStatus=L.text("Windows wird verbunden …");pairingInProgress=true
        do{let client=try PairClient(code.trimmingCharacters(in:.whitespacesAndNewlines),device:history.preferences.device);pendingPair=client;pairingApproved=false
            pairingTask=Task{do{let reply=try await Task.detached{try client.begin()}.value;guard !Task.isCancelled,self.pendingPair?.link.session==client.link.session else{return};guard reply.status != "rejected" else{throw SharedError.unavailable};self.pairingDigits=client.code;self.pairingCanConfirm=true;self.pairingStatus=viaCode ? L.text("Windows gefunden. Erlaube die Verbindung auf diesem Mac und auf dem Windows-PC."):L.text("Vergleiche diesen Bestätigungscode mit Windows.");await self.pollPairing(client)}catch{guard self.pendingPair?.link.session==client.link.session else{return};self.pairingInProgress=false;self.pairingCanConfirm=false;self.pendingPair=nil;self.pairingStatus=L.text("Verbindung nicht möglich. Prüfe das WLAN und die Windows-Freigabe. Der Code muss noch gültig sein.")}}
        }catch{pairingInProgress=false;pairingStatus=L.text("Der QR-Code ist ungültig oder abgelaufen. Erstelle unter Windows einen neuen Code.")}
    }
    func approveQr(){guard pendingPair != nil else{return};pairingApproved=true;pairingCanConfirm=false;pairingStatus=pairingOtherConfirmed ? L.text("Verbindung wird eingerichtet …"):L.text("Auf diesem Mac bestätigt. Bitte jetzt am Windows-PC bestätigen.")}
    func cancelQr(){let previous=pendingPair;pairingTask?.cancel();pairingTask=nil;pendingPair=nil;pairingApproved=false;pairingCanConfirm=false;pairingInProgress=false;pairingDigits="";pairingViaCode=false;pairingOtherConfirmed=false;if let previous{Task.detached{_ = try? previous.action("reject")}}}
    func pollPairing(_ client:PairClient) async {
        while !Task.isCancelled,pendingPair?.link.session==client.link.session {
            if milliseconds()>client.link.expires+(pairingApproved ? 120000:0){pairingStatus=L.text("Der Code ist abgelaufen. Erstelle unter Windows einen neuen Code.");pairingInProgress=false;pairingCanConfirm=false;pendingPair=nil;return}
            let action=pairingApproved ? "approve":"poll"
            do{let reply=try await Task.detached{try client.action(action)}.value;guard !Task.isCancelled,pendingPair?.link.session==client.link.session else{return}
                if reply.status=="complete"{try PairingVault.save(client.pairing);history.preferences.sharingEnabled=true;history.preferences.since=milliseconds();history.preferences.received=[];history.preferences.sent=[];history.preferences.lastWindowsAck="";try history.save();paired=true;pendingPair=nil;pairingDigits="";pairingCanConfirm=false;pairingInProgress=false;pairingStatus=L.text("Mit Windows verbunden. Neue Kopien werden jetzt geteilt.");status=L.text("Mit Windows verbunden");health();return}
                if reply.status=="rejected"{pairingStatus=L.text("Die Kopplung wurde auf Windows abgebrochen.");pairingInProgress=false;pairingCanConfirm=false;pendingPair=nil;return}
                if reply.status=="approved"{pairingOtherConfirmed=true;if !pairingApproved{pairingStatus=pairingViaCode ? L.text("Auf Windows bestätigt. Verbinde jetzt diesen Mac."):L.text("Auf Windows bestätigt. Stimmen die Bestätigungscodes überein?")}}
            }catch{if case SharedError.storage=error{pairingStatus=L.text("Bitte den Schlüsselbundzugriff erlauben. Kopra versucht es erneut.")}else{pairingStatus=L.text("Verbindung unterbrochen. Kopra versucht es erneut.")}}
            try? await Task.sleep(nanoseconds:1_000_000_000)
        }
    }
}
