import Foundation

enum L {
    private static let english=load("en"),german=load("de")
    private static let lock=NSLock()
    private static var current="en"
    static var language:String{lock.lock();defer{lock.unlock()};return current}
    private static func load(_ language:String)->[String:String]{guard let url=Bundle.main.url(forResource:"localization."+language,withExtension:"json"),let data=try? Data(contentsOf:url),let value=try? JSONDecoder().decode([String:String].self,from:data) else{return [:]};return value}
    static func use(_ value:String){lock.lock();current=value=="de" ? "de":"en";lock.unlock();NotificationCenter.default.post(name:Notification.Name("KopraLanguageChanged"),object:nil)}
    static func text(_ source:String)->String{
        if language=="en"{return english[source] ?? source}
        if let value=german[source]{return value}
        if let original=english.first(where:{$0.value==source})?.key{return german[original] ?? original}
        return source
    }
    static func bytes(_ count:Int64)->String{var size=Double(count);let units=["B","KB","MB","GB","TB"];var unit=0;while size>=1024 && unit<units.count-1{size/=1024;unit+=1};let format=NumberFormatter();format.locale=Locale(identifier:language);format.numberStyle = .decimal;format.maximumFractionDigits=unit==0 ? 0:1;return (format.string(from:NSNumber(value:size)) ?? String(count))+" "+units[unit]}
    static func shortcut(_ value:String)->String{language=="de" ? value.replacingOccurrences(of:"Space",with:"Leertaste"):value.replacingOccurrences(of:"Leertaste",with:"Space")}
    static func format(_ source:String,_ values:Any...)->String{
        var result=text(source);let pattern=try! NSRegularExpression(pattern:"\\{([0-9]+)(?::([^}]+))?\\}")
        let original=result as NSString
        for match in pattern.matches(in:result,range:NSRange(location:0,length:original.length)).reversed(){guard let index=Int(original.substring(with:match.range(at:1))),values.indices.contains(index),let range=Range(match.range,in:result) else{continue};var value=String(describing:values[index]);if match.range(at:2).location != NSNotFound{let spec=original.substring(with:match.range(at:2));if spec.hasPrefix("D"),let digits=Int(spec.dropFirst()){value=String(repeating:"0",count:max(0,digits-value.count))+value}};result.replaceSubrange(range,with:value)}
        return result
    }
}
